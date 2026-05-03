using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public enum GameObjectiveType
{
	MutationCount,
	MutationValueAtLeast,
	MutationValueAtMost,
	KillCount,
	KillSpecificTarget,
	TimerCountUp,
	TimerCountdown,
	ReachNotification,
	AvoidNotification,
	ProtectTarget
}

public enum GameObjectiveStatus
{
	Locked,
	Active,
	Completed,
	Failed
}

public enum MutationNotificationSourceFilter
{
	Any,
	FrameworkOnly,
	ExternalOnly
}

[Serializable]
public class GameObjective
{
	[Header("Identity")]
	public string id;
	public string title;
	[TextArea] public string description;

	[Header("Objective")]
	public GameObjectiveType type;
	public GameObjectiveStatus status = GameObjectiveStatus.Active;
	public bool required = true;

	[Header("Mutation Matching")]
	public string mutationKey;
	public string mutationReason;
	public MutationNotificationSourceFilter sourceFilter = MutationNotificationSourceFilter.Any;

	[Header("Progress")]
	public float currentValue;
	public float targetValue = 1f;

	[Header("Timer")]
	public float targetTime = 60f;
	public bool timerRunsOnlyWhenActive = true;

	[Header("Optional Object Filter")]
	public GameObject specificTargetObject;
	public string requiredTargetTag;

	public bool IsDone => status == GameObjectiveStatus.Completed || status == GameObjectiveStatus.Failed;
}

public class GameObjectiveManager : MonoBehaviour
{
	[Header("Objectives")]
	[SerializeField] private List<GameObjective> objectives = new List<GameObjective>();

	[Header("Events")]
	public UnityEvent<GameObjective> onObjectiveUpdated = new UnityEvent<GameObjective>();
	public UnityEvent<GameObjective> onObjectiveCompleted = new UnityEvent<GameObjective>();
	public UnityEvent<GameObjective> onObjectiveFailed = new UnityEvent<GameObjective>();
	public UnityEvent onAllRequiredObjectivesCompleted = new UnityEvent();
	public UnityEvent onAnyRequiredObjectiveFailed = new UnityEvent();

	public IReadOnlyList<GameObjective> Objectives => objectives;

	private bool _completionEmitted;
	private bool _failureEmitted;
	private bool _subscribedToMutationBus;

	private void OnEnable()
	{
		TryRegisterToMutationBus();
	}

	private void Start()
	{
		TryRegisterToMutationBus();
	}

	private void OnDisable()
	{
		UnregisterFromMutationBus();
	}

	private void Update()
	{
		if (!_subscribedToMutationBus)
			TryRegisterToMutationBus();

		float dt = Time.deltaTime;

		foreach (GameObjective objective in objectives)
		{
			if (objective.status != GameObjectiveStatus.Active)
				continue;

			if (objective.timerRunsOnlyWhenActive && objective.status != GameObjectiveStatus.Active)
				continue;

			switch (objective.type)
			{
				case GameObjectiveType.TimerCountUp:
					objective.currentValue += dt;
					onObjectiveUpdated.Invoke(objective);

					if (objective.currentValue >= objective.targetTime)
						CompleteObjective(objective);
					break;

				case GameObjectiveType.TimerCountdown:
					objective.currentValue -= dt;
					onObjectiveUpdated.Invoke(objective);

					if (objective.currentValue <= 0f)
						FailObjective(objective);
					break;
			}
		}

		EvaluateGlobalObjectiveState();
	}

	public void HandleMutationNotification(MutationNotification notification)
	{
		if (notification == null)
			return;

		foreach (GameObjective objective in objectives)
		{
			if (objective.status != GameObjectiveStatus.Active)
				continue;

			if (!MatchesObjective(objective, notification))
				continue;

			ApplyNotificationToObjective(objective, notification);
		}

		EvaluateGlobalObjectiveState();
	}

	private bool MatchesObjective(GameObjective objective, MutationNotification notification)
	{
		if (RequiresDeathNotification(objective.type) && !IsHealthDepletionNotification(notification))
			return false;

		if (objective.type == GameObjectiveType.KillSpecificTarget && objective.specificTargetObject == null)
			return false;

		if (!string.IsNullOrWhiteSpace(objective.mutationKey) &&
			objective.mutationKey != notification.key)
			return false;

		if (!string.IsNullOrWhiteSpace(objective.mutationReason) &&
			objective.mutationReason != notification.reason)
			return false;

		if (objective.specificTargetObject != null &&
			objective.specificTargetObject != notification.target)
			return false;

		switch (objective.sourceFilter)
		{
			case MutationNotificationSourceFilter.FrameworkOnly:
				if (!notification.cameFromFramework)
					return false;
				break;
			case MutationNotificationSourceFilter.ExternalOnly:
				if (notification.cameFromFramework)
					return false;
				break;
		}

		if (!string.IsNullOrWhiteSpace(objective.requiredTargetTag))
		{
			GameObject target = notification.target;

			if (target == null || !target.CompareTag(objective.requiredTargetTag))
				return false;
		}

		return true;
	}

	private void ApplyNotificationToObjective(GameObjective objective, MutationNotification notification)
	{
		switch (objective.type)
		{
			case GameObjectiveType.MutationCount:
				objective.currentValue += 1f;
				onObjectiveUpdated.Invoke(objective);

				if (objective.currentValue >= objective.targetValue)
					CompleteObjective(objective);
				break;

			case GameObjectiveType.MutationValueAtLeast:
				objective.currentValue = notification.newValue;
				onObjectiveUpdated.Invoke(objective);

				if (objective.currentValue >= objective.targetValue)
					CompleteObjective(objective);
				break;

			case GameObjectiveType.MutationValueAtMost:
				objective.currentValue = notification.newValue;
				onObjectiveUpdated.Invoke(objective);

				if (objective.currentValue <= objective.targetValue)
					CompleteObjective(objective);
				break;

			case GameObjectiveType.KillCount:
				objective.currentValue += 1f;
				onObjectiveUpdated.Invoke(objective);

				if (objective.currentValue >= Mathf.Max(1f, objective.targetValue))
					CompleteObjective(objective);
				break;

			case GameObjectiveType.KillSpecificTarget:
				objective.currentValue = 1f;
				onObjectiveUpdated.Invoke(objective);
				CompleteObjective(objective);
				break;

			case GameObjectiveType.ReachNotification:
				objective.currentValue = 1f;
				onObjectiveUpdated.Invoke(objective);
				CompleteObjective(objective);
				break;

			case GameObjectiveType.AvoidNotification:
				objective.currentValue = 1f;
				onObjectiveUpdated.Invoke(objective);
				FailObjective(objective);
				break;

			case GameObjectiveType.ProtectTarget:
				FailObjective(objective);
				break;
		}
	}

	private void CompleteObjective(GameObjective objective)
	{
		if (objective.status != GameObjectiveStatus.Active)
			return;

		objective.status = GameObjectiveStatus.Completed;
		onObjectiveUpdated.Invoke(objective);
		onObjectiveCompleted.Invoke(objective);
	}

	private void FailObjective(GameObjective objective)
	{
		if (objective.status != GameObjectiveStatus.Active)
			return;

		objective.status = GameObjectiveStatus.Failed;
		onObjectiveUpdated.Invoke(objective);
		onObjectiveFailed.Invoke(objective);
	}

	private void EvaluateGlobalObjectiveState()
	{
		bool allRequiredCompleted = true;
		bool anyRequiredFailed = false;

		foreach (GameObjective objective in objectives)
		{
			if (!objective.required)
				continue;

			if (objective.status == GameObjectiveStatus.Failed)
				anyRequiredFailed = true;

			if (objective.status != GameObjectiveStatus.Completed)
				allRequiredCompleted = false;
		}

		if (anyRequiredFailed && !_failureEmitted)
		{
			_failureEmitted = true;
			onAnyRequiredObjectiveFailed.Invoke();
		}

		if (allRequiredCompleted && !_completionEmitted)
		{
			_completionEmitted = true;
			onAllRequiredObjectivesCompleted.Invoke();
		}
	}

	public GameObjective GetObjective(string id)
	{
		return objectives.Find(o => o.id == id);
	}

	public void ResetObjectives()
	{
		_completionEmitted = false;
		_failureEmitted = false;

		foreach (GameObjective objective in objectives)
		{
			objective.status = GameObjectiveStatus.Active;

			if (objective.type == GameObjectiveType.TimerCountdown)
				objective.currentValue = objective.targetTime;
			else
				objective.currentValue = 0f;

			onObjectiveUpdated.Invoke(objective);
		}
	}

	private static bool RequiresDeathNotification(GameObjectiveType objectiveType)
	{
		switch (objectiveType)
		{
			case GameObjectiveType.KillCount:
			case GameObjectiveType.KillSpecificTarget:
				return true;
			default:
				return false;
		}
	}

	private void TryRegisterToMutationBus()
	{
		if (_subscribedToMutationBus || MutationBus.Instance == null)
			return;

		MutationBus.Instance.MutationNotified += HandleMutationNotification;
		_subscribedToMutationBus = true;
	}

	private void UnregisterFromMutationBus()
	{
		if (!_subscribedToMutationBus || MutationBus.Instance == null)
			return;

		MutationBus.Instance.MutationNotified -= HandleMutationNotification;
		_subscribedToMutationBus = false;
	}

	private static bool IsHealthDepletionNotification(MutationNotification notification)
	{
		if (notification == null)
			return false;

		if (notification.key != "health" && notification.key != "lives")
			return false;

		return notification.oldValue > 0f && notification.newValue <= 0f;
	}
}
