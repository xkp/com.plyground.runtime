using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public enum UIWidgetAnchorPreset
{
	Center,
	TopLeft,
	TopCenter,
	TopRight,
	MiddleLeft,
	MiddleRight,
	BottomLeft,
	BottomCenter,
	BottomRight,
	StretchFull
}

public enum UIGameVisibilityMode
{
	Always,
	GameplayOnly,
	PausedOnly,
	GameWonOnly,
	GameLostOnly,
	GameEndedOnly,
	Hidden
}

[Serializable]
public class UIWidgetDefinition
{
	public string id;
	public RectTransform root;
	public UIWidgetAnchorPreset anchorPreset = UIWidgetAnchorPreset.Center;
	public Vector2 anchoredPosition;
	public Vector2 sizeDelta;
	public Vector2 stretchInsetMin;
	public Vector2 stretchInsetMax;
	public bool applyPositionOnAwake = true;
	public bool setAsLastSibling;
	public bool managedByGameState = true;
	public bool manualVisibleOnStart;
	public UIGameVisibilityMode visibilityMode = UIGameVisibilityMode.Always;
}

[Serializable]
public class GameUIWidgetVisibilityEvent : UnityEvent<string, bool> { }

[Serializable]
public class GameUIPauseStateEvent : UnityEvent<bool> { }

[Serializable]
public class GameUIEndGameEvent : UnityEvent<bool> { }

[Serializable]
public class GameUIImageChangedEvent : UnityEvent<string, Sprite> { }

[Serializable]
public class GameUIScoreChangedEvent : UnityEvent<int> { }

[Serializable]
public class GameUIMutationValueChangedEvent : UnityEvent<string, float, GameObject> { }

public class GameUIManager : MonoBehaviour
{
	[Header("References")]
	[SerializeField] private GameObjectiveManager objectiveManager;
	[SerializeField] private List<UIWidgetDefinition> widgets = new List<UIWidgetDefinition>();
	[SerializeField] private List<UIImageSlot> imageSlots = new List<UIImageSlot>();
	[SerializeField] private List<GameUIWidget> widgetComponents = new List<GameUIWidget>();
	[SerializeField] private bool autoCollectImageSlotsFromChildren = true;
	[SerializeField] private bool autoCollectWidgetsFromChildren = true;

	[Header("Pause")]
	[SerializeField] private bool allowPause = true;
	[SerializeField] private bool startPaused;

	[Header("End Game")]
	[SerializeField] private bool endGameWhenObjectivesComplete = true;
	[SerializeField] private bool endGameWhenObjectivesFail = true;
	[SerializeField] private bool pauseTimeOnGameEnd = true;

	[Header("Score")]
	[SerializeField] private int startingScore;

	[Header("Events")]
	public GameUIPauseStateEvent onPauseStateChanged = new GameUIPauseStateEvent();
	public GameUIEndGameEvent onGameEnded = new GameUIEndGameEvent();
	public GameUIWidgetVisibilityEvent onWidgetVisibilityChanged = new GameUIWidgetVisibilityEvent();
	public GameUIImageChangedEvent onImageChanged = new GameUIImageChangedEvent();
	public GameUIScoreChangedEvent onScoreChanged = new GameUIScoreChangedEvent();
	public GameUIMutationValueChangedEvent onMutationValueChanged = new GameUIMutationValueChangedEvent();

	public bool IsPaused => _isPaused;
	public bool HasGameEnded => _hasGameEnded;
	public bool DidPlayerWin => _didPlayerWin;
	public int Score => _score;
	public GameObjectiveManager ObjectiveManager => objectiveManager;

	private readonly Dictionary<string, UIWidgetDefinition> _widgetsById = new Dictionary<string, UIWidgetDefinition>(StringComparer.Ordinal);
	private readonly Dictionary<string, bool> _manualWidgetVisibility = new Dictionary<string, bool>(StringComparer.Ordinal);
	private readonly Dictionary<string, UIImageSlot> _imageSlotsById = new Dictionary<string, UIImageSlot>(StringComparer.Ordinal);
	private readonly Dictionary<string, bool> _appliedWidgetVisibility = new Dictionary<string, bool>(StringComparer.Ordinal);
	private readonly Dictionary<string, GameUIWidget> _widgetComponentsById = new Dictionary<string, GameUIWidget>(StringComparer.Ordinal);
	private readonly Dictionary<string, float> _globalMutationValuesByKey = new Dictionary<string, float>(StringComparer.Ordinal);
	private readonly Dictionary<int, Dictionary<string, float>> _targetMutationValuesByKey = new Dictionary<int, Dictionary<string, float>>();
	private readonly List<GameUIWidget> _objectiveAwareWidgets = new List<GameUIWidget>();

	private bool _didPlayerWin;
	private bool _hasGameEnded;
	private bool _isPaused;
	private bool _objectiveEventsRegistered;
	private bool _mutationEventsRegistered;
	private int _score;

	private void Awake()
	{
		_score = startingScore;
		RebuildCaches();
		ApplyWidgetLayout();
	}

	private void OnEnable()
	{
		RegisterObjectiveEvents();
		RegisterMutationEvents();
	}

	private void Start()
	{
		if (startPaused)
			SetPaused(true);
		else
			RefreshWidgetVisibility();
	}

	private void Update()
	{
		if (!_mutationEventsRegistered)
			RegisterMutationEvents();
	}

	private void OnDisable()
	{
		UnregisterObjectiveEvents();
		UnregisterMutationEvents();
	}

	private void OnValidate()
	{
		ApplyWidgetLayout();
	}

	public void RebuildCaches()
	{
		_widgetsById.Clear();
		_imageSlotsById.Clear();
		_manualWidgetVisibility.Clear();
		_appliedWidgetVisibility.Clear();
		_widgetComponentsById.Clear();
		_objectiveAwareWidgets.Clear();
		_globalMutationValuesByKey.Clear();
		_targetMutationValuesByKey.Clear();

		if (autoCollectImageSlotsFromChildren && (imageSlots == null || imageSlots.Count == 0))
		{
			imageSlots = new List<UIImageSlot>(GetComponentsInChildren<UIImageSlot>(true));
		}

		if (autoCollectWidgetsFromChildren && (widgetComponents == null || widgetComponents.Count == 0))
		{
			widgetComponents = new List<GameUIWidget>(GetComponentsInChildren<GameUIWidget>(true));
		}

		foreach (UIWidgetDefinition widget in widgets)
		{
			if (widget == null || string.IsNullOrWhiteSpace(widget.id))
				continue;

			_widgetsById[widget.id] = widget;
			_manualWidgetVisibility[widget.id] = widget.manualVisibleOnStart;
		}

		foreach (UIImageSlot imageSlot in imageSlots)
		{
			if (imageSlot == null || string.IsNullOrWhiteSpace(imageSlot.SlotId))
				continue;

			_imageSlotsById[imageSlot.SlotId] = imageSlot;
		}

		foreach (GameUIWidget widgetComponent in widgetComponents)
		{
			if (widgetComponent == null || string.IsNullOrWhiteSpace(widgetComponent.WidgetId))
				continue;

			_widgetComponentsById[widgetComponent.WidgetId] = widgetComponent;
			widgetComponent.Bind(this);

			if (widgetComponent is IGameUIObjectiveAwareWidget)
				_objectiveAwareWidgets.Add(widgetComponent);
		}

		RefreshWidgetVisibility();
		BroadcastWidgetRefresh();
	}

	public void TogglePause()
	{
		SetPaused(!_isPaused);
	}

	public void RequestTogglePause()
	{
		TogglePause();
	}

	public void SetPaused(bool paused)
	{
		if (!allowPause && paused)
			return;

		if (_hasGameEnded && paused)
			return;

		if (_isPaused == paused)
		{
			RefreshWidgetVisibility();
			return;
		}

		_isPaused = paused;
		Time.timeScale = paused ? 0f : 1f;
		onPauseStateChanged.Invoke(_isPaused);
		RefreshWidgetVisibility();
	}

	public void ResumeGame()
	{
		SetPaused(false);
	}

	public void RequestPause()
	{
		SetPaused(true);
	}

	public void RequestResume()
	{
		ResumeGame();
	}

	public void EndGame(bool playerWon)
	{
		if (_hasGameEnded && _didPlayerWin == playerWon)
		{
			RefreshWidgetVisibility();
			return;
		}

		_hasGameEnded = true;
		_didPlayerWin = playerWon;
		_isPaused = false;

		if (pauseTimeOnGameEnd)
			Time.timeScale = 0f;

		onGameEnded.Invoke(playerWon);
		RefreshWidgetVisibility();
	}

	public void ResetUIState()
	{
		_hasGameEnded = false;
		_didPlayerWin = false;
		_isPaused = false;
		_score = startingScore;
		Time.timeScale = 1f;

		foreach (UIWidgetDefinition widget in widgets)
		{
			if (widget == null || string.IsNullOrWhiteSpace(widget.id))
				continue;

			_manualWidgetVisibility[widget.id] = widget.manualVisibleOnStart;
		}

		foreach (UIImageSlot imageSlot in imageSlots)
		{
			imageSlot?.ResetToDefault();
		}

		onScoreChanged.Invoke(_score);
		RefreshWidgetVisibility();
		BroadcastWidgetRefresh();
	}

	public bool ShowWidget(string widgetId)
	{
		return SetWidgetVisible(widgetId, true);
	}

	public bool HideWidget(string widgetId)
	{
		return SetWidgetVisible(widgetId, false);
	}

	public bool SetWidgetVisible(string widgetId, bool visible)
	{
		if (!_widgetsById.ContainsKey(widgetId))
			return false;

		_manualWidgetVisibility[widgetId] = visible;
		RefreshWidgetVisibility();
		return true;
	}

	public bool SetWidgetAnchorPreset(string widgetId, UIWidgetAnchorPreset anchorPreset, Vector2 anchoredPosition)
	{
		if (!_widgetsById.TryGetValue(widgetId, out UIWidgetDefinition widget) || widget.root == null)
			return false;

		widget.anchorPreset = anchorPreset;
		widget.anchoredPosition = anchoredPosition;
		ApplyLayout(widget);
		return true;
	}

	public bool SetImage(string slotId, Sprite sprite)
	{
		if (!_imageSlotsById.TryGetValue(slotId, out UIImageSlot imageSlot))
			return false;

		imageSlot.ApplySprite(sprite);
		onImageChanged.Invoke(slotId, sprite);
		return true;
	}

	public void AddScore(int amount)
	{
		SetScore(_score + amount);
	}

	public void SetScore(int score)
	{
		if (_score == score)
			return;

		_score = score;
		onScoreChanged.Invoke(_score);

		foreach (GameUIWidget widgetComponent in _widgetComponentsById.Values)
		{
			widgetComponent.HandleScoreChanged(_score);
		}
	}

	public bool SetWidgetText(string widgetId, string value)
	{
		if (!_widgetComponentsById.TryGetValue(widgetId, out GameUIWidget widgetComponent))
			return false;

		if (widgetComponent is GameUITextWidget textWidget)
		{
			textWidget.SetText(value);
			return true;
		}

		return false;
	}

	public T GetWidget<T>(string widgetId) where T : GameUIWidget
	{
		if (_widgetComponentsById.TryGetValue(widgetId, out GameUIWidget widgetComponent))
			return widgetComponent as T;

		return null;
	}

	public bool ResetImage(string slotId)
	{
		if (!_imageSlotsById.TryGetValue(slotId, out UIImageSlot imageSlot))
			return false;

		imageSlot.ResetToDefault();
		onImageChanged.Invoke(slotId, imageSlot.CurrentSprite);
		return true;
	}

	public bool TryGetMutationValue(string mutationKey, out float value)
	{
		if (string.IsNullOrWhiteSpace(mutationKey))
		{
			value = default;
			return false;
		}

		return _globalMutationValuesByKey.TryGetValue(mutationKey, out value);
	}

	public bool TryGetMutationValue(string mutationKey, GameObject target, out float value)
	{
		if (string.IsNullOrWhiteSpace(mutationKey) || target == null)
		{
			value = default;
			return false;
		}

		if (_targetMutationValuesByKey.TryGetValue(target.GetInstanceID(), out Dictionary<string, float> valuesForTarget) &&
			valuesForTarget.TryGetValue(mutationKey, out value))
		{
			return true;
		}

		return _globalMutationValuesByKey.TryGetValue(mutationKey, out value);
	}

	public float GetMutationValueOrDefault(string mutationKey, float fallbackValue = 0f)
	{
		return TryGetMutationValue(mutationKey, out float value) ? value : fallbackValue;
	}

	public float GetMutationValueOrDefault(string mutationKey, GameObject target, float fallbackValue = 0f)
	{
		return TryGetMutationValue(mutationKey, target, out float value) ? value : fallbackValue;
	}

	private void ApplyWidgetLayout()
	{
		foreach (UIWidgetDefinition widget in widgets)
		{
			ApplyLayout(widget);
		}
	}

	private void ApplyLayout(UIWidgetDefinition widget)
	{
		if (widget == null || widget.root == null || !widget.applyPositionOnAwake)
			return;

		switch (widget.anchorPreset)
		{
			case UIWidgetAnchorPreset.TopLeft:
				SetAnchor(widget.root, new Vector2(0f, 1f), new Vector2(0f, 1f));
				break;
			case UIWidgetAnchorPreset.TopCenter:
				SetAnchor(widget.root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
				break;
			case UIWidgetAnchorPreset.TopRight:
				SetAnchor(widget.root, new Vector2(1f, 1f), new Vector2(1f, 1f));
				break;
			case UIWidgetAnchorPreset.MiddleLeft:
				SetAnchor(widget.root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
				break;
			case UIWidgetAnchorPreset.MiddleRight:
				SetAnchor(widget.root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
				break;
			case UIWidgetAnchorPreset.BottomLeft:
				SetAnchor(widget.root, new Vector2(0f, 0f), new Vector2(0f, 0f));
				break;
			case UIWidgetAnchorPreset.BottomCenter:
				SetAnchor(widget.root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
				break;
			case UIWidgetAnchorPreset.BottomRight:
				SetAnchor(widget.root, new Vector2(1f, 0f), new Vector2(1f, 0f));
				break;
			case UIWidgetAnchorPreset.StretchFull:
				widget.root.anchorMin = Vector2.zero;
				widget.root.anchorMax = Vector2.one;
				widget.root.offsetMin = widget.stretchInsetMin;
				widget.root.offsetMax = -widget.stretchInsetMax;
				break;
			default:
				SetAnchor(widget.root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
				break;
		}

		if (widget.anchorPreset != UIWidgetAnchorPreset.StretchFull)
		{
			widget.root.anchoredPosition = widget.anchoredPosition;
			widget.root.sizeDelta = widget.sizeDelta;
		}

		if (widget.setAsLastSibling)
			widget.root.SetAsLastSibling();
	}

	private static void SetAnchor(RectTransform rectTransform, Vector2 anchorMin, Vector2 anchorMax)
	{
		rectTransform.anchorMin = anchorMin;
		rectTransform.anchorMax = anchorMax;
		rectTransform.offsetMin = Vector2.zero;
		rectTransform.offsetMax = Vector2.zero;
	}

	private void RefreshWidgetVisibility()
	{
		foreach (UIWidgetDefinition widget in widgets)
		{
			if (widget == null || widget.root == null || string.IsNullOrWhiteSpace(widget.id))
				continue;

			bool isVisible = EvaluateWidgetVisibility(widget);

			if (!_appliedWidgetVisibility.TryGetValue(widget.id, out bool previousVisibility) || previousVisibility != isVisible)
			{
				_appliedWidgetVisibility[widget.id] = isVisible;
				onWidgetVisibilityChanged.Invoke(widget.id, isVisible);

				if (_widgetComponentsById.TryGetValue(widget.id, out GameUIWidget widgetComponent))
					widgetComponent.HandleVisibilityChanged(isVisible);
			}

			if (widget.root.gameObject.activeSelf != isVisible)
				widget.root.gameObject.SetActive(isVisible);
		}
	}

	private bool EvaluateWidgetVisibility(UIWidgetDefinition widget)
	{
		if (!widget.managedByGameState)
			return _manualWidgetVisibility.TryGetValue(widget.id, out bool manualVisible) && manualVisible;

		switch (widget.visibilityMode)
		{
			case UIGameVisibilityMode.GameplayOnly:
				return !_isPaused && !_hasGameEnded;
			case UIGameVisibilityMode.PausedOnly:
				return _isPaused && !_hasGameEnded;
			case UIGameVisibilityMode.GameWonOnly:
				return _hasGameEnded && _didPlayerWin;
			case UIGameVisibilityMode.GameLostOnly:
				return _hasGameEnded && !_didPlayerWin;
			case UIGameVisibilityMode.GameEndedOnly:
				return _hasGameEnded;
			case UIGameVisibilityMode.Hidden:
				return false;
			default:
				return true;
		}
	}

	private void RegisterObjectiveEvents()
	{
		if (_objectiveEventsRegistered)
			return;

		if (objectiveManager == null)
			objectiveManager = GetComponent<GameObjectiveManager>();

		if (objectiveManager == null)
			return;

		objectiveManager.onAllRequiredObjectivesCompleted.AddListener(HandleObjectivesCompleted);
		objectiveManager.onAnyRequiredObjectiveFailed.AddListener(HandleObjectivesFailed);
		objectiveManager.onObjectiveUpdated.AddListener(HandleObjectiveUpdated);
		_objectiveEventsRegistered = true;
	}

	private void UnregisterObjectiveEvents()
	{
		if (!_objectiveEventsRegistered || objectiveManager == null)
			return;

		objectiveManager.onAllRequiredObjectivesCompleted.RemoveListener(HandleObjectivesCompleted);
		objectiveManager.onAnyRequiredObjectiveFailed.RemoveListener(HandleObjectivesFailed);
		objectiveManager.onObjectiveUpdated.RemoveListener(HandleObjectiveUpdated);
		_objectiveEventsRegistered = false;
	}

	private void HandleObjectiveUpdated(GameObjective objective)
	{
		for (int i = 0; i < _objectiveAwareWidgets.Count; i++)
		{
			if (_objectiveAwareWidgets[i] is IGameUIObjectiveAwareWidget objectiveAwareWidget)
				objectiveAwareWidget.HandleObjectiveUpdated(objective);
		}
	}

	private void RegisterMutationEvents()
	{
		if (_mutationEventsRegistered || MutationBus.Instance == null)
			return;

		MutationBus.Instance.MutationNotified += HandleMutationNotification;
		_mutationEventsRegistered = true;
	}

	private void UnregisterMutationEvents()
	{
		if (!_mutationEventsRegistered || MutationBus.Instance == null)
			return;

		MutationBus.Instance.MutationNotified -= HandleMutationNotification;
		_mutationEventsRegistered = false;
	}

	private void HandleMutationNotification(MutationNotification notification)
	{
		if (notification == null || string.IsNullOrWhiteSpace(notification.key))
			return;

		_globalMutationValuesByKey[notification.key] = notification.newValue;

		if (notification.target != null)
		{
			int targetId = notification.target.GetInstanceID();

			if (!_targetMutationValuesByKey.TryGetValue(targetId, out Dictionary<string, float> valuesForTarget))
			{
				valuesForTarget = new Dictionary<string, float>(StringComparer.Ordinal);
				_targetMutationValuesByKey[targetId] = valuesForTarget;
			}

			valuesForTarget[notification.key] = notification.newValue;
		}

		onMutationValueChanged.Invoke(notification.key, notification.newValue, notification.target);

		foreach (GameUIWidget widgetComponent in _widgetComponentsById.Values)
		{
			widgetComponent.HandleMutationChanged(notification);
		}
	}

	private void HandleObjectivesCompleted()
	{
		if (endGameWhenObjectivesComplete)
			EndGame(true);

		BroadcastWidgetRefresh();
	}

	private void HandleObjectivesFailed()
	{
		if (endGameWhenObjectivesFail)
			EndGame(false);

		BroadcastWidgetRefresh();
	}

	private void BroadcastWidgetRefresh()
	{
		foreach (GameUIWidget widgetComponent in _widgetComponentsById.Values)
		{
			widgetComponent.RefreshWidget();
		}
	}
}

public abstract class GameUIWidget : MonoBehaviour
{
	[SerializeField] private string widgetId;

	protected GameUIManager Manager { get; private set; }

	public string WidgetId => widgetId;

	public void Bind(GameUIManager manager)
	{
		Manager = manager;
		OnBound();
		RefreshWidget();
	}

	public virtual void HandleScoreChanged(int score) { }

	public virtual void HandleVisibilityChanged(bool isVisible) { }

	public virtual void HandleMutationChanged(MutationNotification notification) { }

	public virtual void RefreshWidget() { }

	protected virtual void OnBound() { }
}

public interface IGameUIObjectiveAwareWidget
{
	void HandleObjectiveUpdated(GameObjective objective);
}

public class GameUITextWidget : GameUIWidget
{
	[SerializeField] private Text targetText;
	[SerializeField] private string prefix;
	[SerializeField] private string suffix;
	[SerializeField] private string defaultText;

	private string _currentValue;
	private bool _hasExplicitValue;

	protected override void OnBound()
	{
		if (targetText == null)
			targetText = GetComponent<Text>();
	}

	public override void RefreshWidget()
	{
		if (!_hasExplicitValue)
			ApplyRawText(defaultText);
		else
			ApplyRawText(_currentValue);
	}

	public void SetText(string value)
	{
		_currentValue = value ?? string.Empty;
		_hasExplicitValue = true;
		ApplyRawText(_currentValue);
	}

	protected void ApplyRawText(string value)
	{
		if (targetText == null)
			return;

		targetText.text = $"{prefix}{value}{suffix}";
	}
}

public class UIScoreTextWidget : GameUITextWidget
{
	[SerializeField] private string scoreFormat = "0";
	[SerializeField] private string emptyText = "0";

	public override void HandleScoreChanged(int score)
	{
		SetText(score.ToString(scoreFormat));
	}

	public override void RefreshWidget()
	{
		if (Manager == null)
		{
			SetText(emptyText);
			return;
		}

		SetText(Manager.Score.ToString(scoreFormat));
	}
}

public class UIObjectiveSummaryWidget : GameUITextWidget, IGameUIObjectiveAwareWidget
{
	[SerializeField] private GameObjectiveManager objectiveManager;
	[SerializeField] private string format = "{0}/{1} Objectives";
	[SerializeField] private string noObjectivesText = "No Objectives";

	protected override void OnBound()
	{
		base.OnBound();

		if (objectiveManager == null)
			objectiveManager = Manager != null ? Manager.ObjectiveManager : GetComponentInParent<GameObjectiveManager>();
	}

	public override void RefreshWidget()
	{
		if (objectiveManager == null || objectiveManager.Objectives == null || objectiveManager.Objectives.Count == 0)
		{
			SetText(noObjectivesText);
			return;
		}

		int requiredCount = 0;
		int completedCount = 0;

		for (int i = 0; i < objectiveManager.Objectives.Count; i++)
		{
			GameObjective objective = objectiveManager.Objectives[i];

			if (!objective.required)
				continue;

			requiredCount++;

			if (objective.status == GameObjectiveStatus.Completed)
				completedCount++;
		}

		if (requiredCount == 0)
		{
			SetText(noObjectivesText);
			return;
		}

		SetText(string.Format(format, completedCount, requiredCount));
	}

	public void HandleObjectiveUpdated(GameObjective objective)
	{
		RefreshWidget();
	}
}

public class UIMutationTextWidget : GameUITextWidget
{
	[SerializeField] private string mutationKey = "health";
	[SerializeField] private GameObject targetOverride;
	[SerializeField] private bool useManagerGameObjectAsTarget;
	[SerializeField] private string valueFormat = "0";
	[SerializeField] private string missingValueText = "--";
	[SerializeField] private bool refreshOnlyForMatchingMutation = true;

	public override void HandleMutationChanged(MutationNotification notification)
	{
		if (notification == null)
			return;

		if (refreshOnlyForMatchingMutation)
		{
			if (!string.Equals(notification.key, mutationKey, StringComparison.Ordinal))
				return;

			GameObject desiredTarget = ResolveTarget();

			if (desiredTarget != null && notification.target != desiredTarget)
				return;
		}

		RefreshWidget();
	}

	public override void RefreshWidget()
	{
		if (Manager == null || string.IsNullOrWhiteSpace(mutationKey))
		{
			SetText(missingValueText);
			return;
		}

		GameObject desiredTarget = ResolveTarget();
		float value;
		bool foundValue;

		if (desiredTarget != null)
			foundValue = Manager.TryGetMutationValue(mutationKey, desiredTarget, out value);
		else
			foundValue = Manager.TryGetMutationValue(mutationKey, out value);

		if (!foundValue)
		{
			SetText(missingValueText);
			return;
		}

		SetText(value.ToString(valueFormat));
	}

	private GameObject ResolveTarget()
	{
		if (targetOverride != null)
			return targetOverride;

		if (useManagerGameObjectAsTarget && Manager != null)
			return Manager.gameObject;

		return null;
	}
}

public class UIMutationProgressBarWidget : GameUIWidget
{
	[SerializeField] private string currentMutationKey = "health";
	[SerializeField] private string maxMutationKey = "maxHealth";
	[SerializeField] private GameObject targetOverride;
	[SerializeField] private bool useManagerGameObjectAsTarget;
	[SerializeField] private Image fillImage;
	[SerializeField] private Slider targetSlider;
	[SerializeField] private bool preferFilledImage = true;
	[SerializeField] private float defaultMinValue = 0f;
	[SerializeField] private float defaultMaxValue = 100f;
	[SerializeField] private bool clampBetweenMinMax = true;
	[SerializeField] private bool hideWhenNoValue;
	[SerializeField] private bool refreshOnlyForMatchingMutation = true;

	protected override void OnBound()
	{
		if (fillImage == null)
			fillImage = GetComponent<Image>();

		if (targetSlider == null)
			targetSlider = GetComponent<Slider>();
	}

	public override void HandleMutationChanged(MutationNotification notification)
	{
		if (notification == null)
			return;

		if (refreshOnlyForMatchingMutation)
		{
			bool matchesCurrent = string.Equals(notification.key, currentMutationKey, StringComparison.Ordinal);
			bool matchesMax = string.Equals(notification.key, maxMutationKey, StringComparison.Ordinal);

			if (!matchesCurrent && !matchesMax)
				return;

			GameObject desiredTarget = ResolveTarget();

			if (desiredTarget != null && notification.target != desiredTarget)
				return;
		}

		RefreshWidget();
	}

	public override void RefreshWidget()
	{
		GameObject desiredTarget = ResolveTarget();
		float currentValue;
		float maxValue;
		bool hasCurrentValue = TryResolveMutationValue(currentMutationKey, desiredTarget, defaultMinValue, out currentValue);
		bool hasMaxValue = TryResolveMutationValue(maxMutationKey, desiredTarget, defaultMaxValue, out maxValue);

		if (!hasCurrentValue && hideWhenNoValue)
		{
			SetTargetsEnabled(false);
			return;
		}

		SetTargetsEnabled(true);

		if (Mathf.Approximately(maxValue, 0f))
			maxValue = defaultMaxValue <= 0f ? 1f : defaultMaxValue;

		if (clampBetweenMinMax)
			currentValue = Mathf.Clamp(currentValue, defaultMinValue, maxValue);

		float normalizedValue = Mathf.InverseLerp(defaultMinValue, maxValue, currentValue);

		if (targetSlider != null)
		{
			targetSlider.minValue = defaultMinValue;
			targetSlider.maxValue = maxValue;
			targetSlider.value = currentValue;
		}

		if (fillImage != null && preferFilledImage)
			fillImage.fillAmount = normalizedValue;
	}

	private bool TryResolveMutationValue(string mutationKey, GameObject target, float fallbackValue, out float value)
	{
		if (Manager == null || string.IsNullOrWhiteSpace(mutationKey))
		{
			value = fallbackValue;
			return false;
		}

		bool foundValue;

		if (target != null)
			foundValue = Manager.TryGetMutationValue(mutationKey, target, out value);
		else
			foundValue = Manager.TryGetMutationValue(mutationKey, out value);

		if (!foundValue)
			value = fallbackValue;

		return foundValue;
	}

	private GameObject ResolveTarget()
	{
		if (targetOverride != null)
			return targetOverride;

		if (useManagerGameObjectAsTarget && Manager != null)
			return Manager.gameObject;

		return null;
	}

	private void SetTargetsEnabled(bool enabled)
	{
		if (fillImage != null)
			fillImage.enabled = enabled;

		if (targetSlider != null)
			targetSlider.enabled = enabled;
	}
}

public class UIImageSlot : MonoBehaviour
{
	[SerializeField] private string slotId;
	[SerializeField] private Image targetImage;
	[SerializeField] private Sprite defaultSprite;
	[SerializeField] private bool disableWhenSpriteMissing;
	[SerializeField] private bool preserveAspect = true;

	public string SlotId => slotId;
	public Sprite CurrentSprite => targetImage != null ? targetImage.sprite : null;

	private void Awake()
	{
		if (targetImage == null)
			targetImage = GetComponent<Image>();

		if (targetImage != null && defaultSprite == null)
			defaultSprite = targetImage.sprite;

		ApplySprite(defaultSprite);
	}

	public void ApplySprite(Sprite sprite)
	{
		if (targetImage == null)
			return;

		targetImage.sprite = sprite;
		targetImage.preserveAspect = preserveAspect;

		if (disableWhenSpriteMissing)
			targetImage.enabled = sprite != null;
	}

	public void ResetToDefault()
	{
		ApplySprite(defaultSprite);
	}
}

public class GameUIPauseInputListener : MonoBehaviour
{
	[SerializeField] private GameUIManager targetManager;
	[SerializeField] private bool listenForPauseKey = true;
	[SerializeField] private KeyCode pauseKey = KeyCode.Escape;

	private void Reset()
	{
		if (targetManager == null)
			targetManager = GetComponent<GameUIManager>();
	}

	private void Awake()
	{
		if (targetManager == null)
			targetManager = GetComponent<GameUIManager>();
	}

	private void Update()
	{
		if (targetManager == null || !listenForPauseKey)
			return;

		if (Input.GetKeyDown(pauseKey))
			targetManager.RequestTogglePause();
	}
}
