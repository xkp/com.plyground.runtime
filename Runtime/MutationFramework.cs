using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public enum MutationOperation
{
	Add,
	Subtract,
	Set,
	Toggle
}

[Serializable]
public class MutationRequest
{
	public string key;
	public MutationOperation operation;
	public float value;

	public GameObject target;
	public GameObject source;

	public string reason;

	// Runtime-only, non-Unity-serializable custom data.
	[NonSerialized] public object customPayload;
	[NonSerialized] public Dictionary<string, object> customData;
}

[Serializable]
public class MutationNotification
{
	public string key;

	public float oldValue;
	public float newValue;
	public float delta;

	public GameObject target;
	public GameObject source;

	public bool cameFromFramework;
	public string reason;

	// Runtime-only, non-Unity-serializable custom data.
	[NonSerialized] public object customPayload;
	[NonSerialized] public Dictionary<string, object> customData;
}

[Serializable]
public class MutationRequestEvent : UnityEvent<MutationRequest> { }

[Serializable]
public class MutationNotificationEvent : UnityEvent<MutationNotification> { }

public interface IMutationAdapter
{
	bool CanHandle(MutationRequest request);
	void Handle(MutationRequest request);
}

public class MutationBus : MonoBehaviour
{
	public static MutationBus Instance { get; private set; }

	[Header("Persistent Unity Events")]
	public MutationRequestEvent onMutationRequested;
	public MutationNotificationEvent onMutationNotified;

	public event Action<MutationRequest> RequestReceived;
	public event Action<MutationNotification> MutationNotified;

	private readonly List<IMutationAdapter> _adapters = new();

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;
	}

	public void RegisterAdapter(IMutationAdapter adapter)
	{
		if (adapter == null)
			return;

		if (!_adapters.Contains(adapter))
			_adapters.Add(adapter);
	}

	public void UnregisterAdapter(IMutationAdapter adapter)
	{
		if (adapter == null)
			return;

		_adapters.Remove(adapter);
	}

	public void Request(MutationRequest request)
	{
		if (request == null)
			return;

		onMutationRequested?.Invoke(request);
		RequestReceived?.Invoke(request);

		for (int i = 0; i < _adapters.Count; i++)
		{
			var adapter = _adapters[i];

			if (adapter != null && adapter.CanHandle(request))
			{
				adapter.Handle(request);
				return;
			}
		}

		Debug.LogWarning($"No mutation adapter handled key '{request.key}' for target '{request.target}'.");
	}

	public void Notify(MutationNotification notification)
	{
		if (notification == null)
			return;

		onMutationNotified?.Invoke(notification);
		MutationNotified?.Invoke(notification);
	}
}

public abstract class MutationAdapterBase : MonoBehaviour, IMutationAdapter
{
	protected virtual void OnEnable()
	{
		if (MutationBus.Instance != null)
			MutationBus.Instance.RegisterAdapter(this);
	}

	protected virtual void OnDisable()
	{
		if (MutationBus.Instance != null)
			MutationBus.Instance.UnregisterAdapter(this);
	}

	public abstract bool CanHandle(MutationRequest request);

	public abstract void Handle(MutationRequest request);
}

/*
Example third-party component.
Replace this with your real module/component.
*/
public class HealthComponent : MonoBehaviour
{
	public event Action<float, float> onHealthChanged;

	[SerializeField] private float health = 100f;

	public float Health => health;

	public void SetHealth(float value)
	{
		float oldValue = health;
		health = Mathf.Max(0f, value);

		if (!Mathf.Approximately(oldValue, health))
			onHealthChanged?.Invoke(oldValue, health);
	}
}

/*
Example adapter for a third-party HealthComponent.
This handles framework requests AND external health changes.
*/
public class HealthMutationAdapter : MutationAdapterBase
{
	[SerializeField] private HealthComponent health;

	private bool _isFrameworkMutation;

	private void Awake()
	{
		if (health == null)
			health = GetComponent<HealthComponent>();
	}

	protected override void OnEnable()
	{
		base.OnEnable();

		if (health != null)
			health.onHealthChanged += OnExternalHealthChanged;
	}

	protected override void OnDisable()
	{
		if (health != null)
			health.onHealthChanged -= OnExternalHealthChanged;

		base.OnDisable();
	}

	public override bool CanHandle(MutationRequest request)
	{
		return request != null
			&& request.key == "health"
			&& request.target == gameObject
			&& health != null;
	}

	public override void Handle(MutationRequest request)
	{
		float oldValue = health.Health;
		float requestedValue = oldValue;

		switch (request.operation)
		{
			case MutationOperation.Add:
				requestedValue = oldValue + request.value;
				break;

			case MutationOperation.Subtract:
				requestedValue = oldValue - request.value;
				break;

			case MutationOperation.Set:
				requestedValue = request.value;
				break;

			case MutationOperation.Toggle:
				requestedValue = Mathf.Approximately(oldValue, 0f) ? 1f : 0f;
				break;
		}

		_isFrameworkMutation = true;
		health.SetHealth(requestedValue);
		_isFrameworkMutation = false;

		MutationBus.Instance.Notify(new MutationNotification
		{
			key = request.key,
			oldValue = oldValue,
			newValue = health.Health,
			delta = health.Health - oldValue,
			target = gameObject,
			source = request.source,
			cameFromFramework = true,
			reason = request.reason,
			customPayload = request.customPayload,
			customData = request.customData
		});
	}

	private void OnExternalHealthChanged(float oldValue, float newValue)
	{
		if (_isFrameworkMutation)
			return;

		MutationBus.Instance.Notify(new MutationNotification
		{
			key = "health",
			oldValue = oldValue,
			newValue = newValue,
			delta = newValue - oldValue,
			target = gameObject,
			source = gameObject,
			cameFromFramework = false,
			reason = "External health change"
		});
	}
}

/*
Example listener.
*/
public class MutationDebugListener : MonoBehaviour
{
	private void OnEnable()
	{
		if (MutationBus.Instance != null)
			MutationBus.Instance.MutationNotified += OnMutation;
	}

	private void OnDisable()
	{
		if (MutationBus.Instance != null)
			MutationBus.Instance.MutationNotified -= OnMutation;
	}

	private void OnMutation(MutationNotification notification)
	{
		Debug.Log(
			$"Mutation: {notification.key} {notification.oldValue} -> {notification.newValue}, " +
			$"Target: {notification.target}, Framework: {notification.cameFromFramework}"
		);
	}
}

/*
Example requester.
*/
public class DamageTester : MonoBehaviour
{
	[SerializeField] private GameObject target;

	public void DamageTarget()
	{
		MutationBus.Instance.Request(new MutationRequest
		{
			key = "health",
			operation = MutationOperation.Subtract,
			value = 10f,
			target = target,
			source = gameObject,
			reason = "Damage tester",

			customPayload = new DamageInfo
			{
				damageType = "fire",
				critical = true
			},

			customData = new Dictionary<string, object>
			{
				{ "weaponId", "axe_001" },
				{ "comboIndex", 3 },
				{ "wasChargedAttack", true }
			}
		});
	}
}

public class DamageInfo
{
	public string damageType;
	public bool critical;
}