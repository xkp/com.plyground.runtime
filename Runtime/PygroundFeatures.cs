// PlygroundNativeFeatures.ExecutionMode.cs
// Generated/updated against default-feature-catalog.json.
// Execution modes:
// - reactive: state/event components; no action lifecycle unless explicitly needed.
// - sync: StartAction/Trigger-style actions complete immediately and expose OnCompleted when catalog includes it.
// - async: long-running or flow-control actions with StartAction/CancelAction/PauseAction/ResumeAction lifecycle.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.AI;
using UnityEngine.UI;

namespace Plyground.Features
{
    [Serializable] public class BoolEvent : UnityEvent<bool> {}
    [Serializable] public class FloatEvent : UnityEvent<float> {}
    [Serializable] public class IntEvent : UnityEvent<int> {}
    [Serializable] public class StringEvent : UnityEvent<string> {}
    [Serializable] public class GameObjectEvent : UnityEvent<GameObject> {}
    [Serializable] public class Vector3Event : UnityEvent<Vector3> {}

    public abstract class PlyFeature : MonoBehaviour
    {
        [SerializeField] protected bool enabledFeature = true;
        public bool IsEnabled => enabledFeature;

        public virtual void Enable(bool value = true) => enabledFeature = value;
        public virtual void Disable(bool value = true) => enabledFeature = !value;
    }

    public abstract class PlyReactiveFeature : PlyFeature
    {
        // Marker base for executionMode = reactive.
        // Reactive features respond to inputs and emit outputs directly.
    }

    public abstract class PlySyncFeature : PlyFeature
    {
        // Base for executionMode = sync.
        // These actions complete immediately when triggered.
        public UnityEvent OnCompleted = new();

        public virtual void StartAction()
        {
            if (!enabledFeature) return;
            Complete();
        }

        protected virtual void Complete() => OnCompleted.Invoke();
    }

    public abstract class PlyAsyncFeature : PlyFeature
    {
        // Base for executionMode = async.
        public UnityEvent OnStarted = new();
        public UnityEvent OnCompleted = new();
        public StringEvent OnFailed = new();
        public UnityEvent OnCancelled = new();

        protected bool running;
        protected bool paused;
        protected Coroutine runningRoutine;

        public bool IsRunning => running;
        public bool IsPaused => paused;

        public virtual void StartAction()
        {
            if (!enabledFeature || running) return;
            running = true;
            paused = false;
            OnStarted.Invoke();
        }

        public virtual void CancelAction()
        {
            if (!running) return;
            running = false;
            paused = false;

            if (runningRoutine != null)
            {
                StopCoroutine(runningRoutine);
                runningRoutine = null;
            }

            OnCancelled.Invoke();
        }

        public virtual void PauseAction()
        {
            if (running) paused = true;
        }

        public virtual void ResumeAction()
        {
            if (running) paused = false;
        }

        protected virtual void Complete()
        {
            running = false;
            paused = false;
            runningRoutine = null;
            OnCompleted.Invoke();
        }

        protected virtual void Fail(string reason)
        {
            running = false;
            paused = false;
            runningRoutine = null;
            OnFailed.Invoke(reason);
        }

        protected IEnumerator WaitWhilePaused()
        {
            while (paused && running)
                yield return null;
        }
    }

    // =========================================================
    // REACTIVE SENSORS
    // =========================================================

    public class PlyLineOfSightSensor : PlyReactiveFeature
    {
        public Transform source;
        public Transform target;
        public float range = 20f;
        public float fieldOfViewDegrees = 120f;
        public LayerMask obstructionMask = ~0;

        public UnityEvent OnTargetVisible = new();
        public UnityEvent OnTargetHidden = new();
        public BoolEvent OnVisibilityChanged = new();

        private bool wasVisible;

        private void Reset() => source = transform;

        private void Update()
        {
            if (!enabledFeature || source == null || target == null) return;

            bool visible = CanSeeTarget();
            if (visible == wasVisible) return;

            wasVisible = visible;
            OnVisibilityChanged.Invoke(visible);
            if (visible) OnTargetVisible.Invoke();
            else OnTargetHidden.Invoke();
        }

        private bool CanSeeTarget()
        {
            Vector3 delta = target.position - source.position;
            float distance = delta.magnitude;

            if (distance > range) return false;
            if (Vector3.Angle(source.forward, delta.normalized) > fieldOfViewDegrees * 0.5f) return false;

            if (Physics.Raycast(source.position, delta.normalized, out var hit, distance, obstructionMask, QueryTriggerInteraction.Ignore))
                return hit.transform == target || hit.transform.IsChildOf(target);

            return true;
        }
    }

    public class PlyVisionConeSensor : PlyReactiveFeature
    {
        public Transform source;
        public string targetTag = "Player";
        public float range = 15f;
        public float coneAngleDegrees = 90f;

        public UnityEvent OnTargetEnteredCone = new();
        public UnityEvent OnTargetExitedCone = new();
        public IntEvent TargetsVisible = new();

        private readonly HashSet<GameObject> visible = new();

        private void Reset() => source = transform;

        private void Update()
        {
            if (!enabledFeature || source == null || string.IsNullOrEmpty(targetTag)) return;

            var candidates = GameObject.FindGameObjectsWithTag(targetTag);
            var nowVisible = new HashSet<GameObject>();

            foreach (var obj in candidates)
            {
                Vector3 delta = obj.transform.position - source.position;
                if (delta.magnitude > range) continue;
                if (Vector3.Angle(source.forward, delta.normalized) <= coneAngleDegrees * 0.5f)
                    nowVisible.Add(obj);
            }

            foreach (var obj in nowVisible)
                if (!visible.Contains(obj)) OnTargetEnteredCone.Invoke();

            foreach (var obj in visible)
                if (!nowVisible.Contains(obj)) OnTargetExitedCone.Invoke();

            visible.Clear();
            foreach (var obj in nowVisible) visible.Add(obj);
            TargetsVisible.Invoke(visible.Count);
        }
    }

    public class PlyProximityTrigger : PlyReactiveFeature
    {
        public Transform source;
        public Transform target;
        public float radius = 5f;

        public UnityEvent OnEnter = new();
        public UnityEvent OnExit = new();
        public BoolEvent Inside = new();

        private bool inside;

        private void Reset() => source = transform;

        private void Update()
        {
            if (!enabledFeature || source == null || target == null) return;

            bool nowInside = Vector3.Distance(source.position, target.position) <= radius;
            if (nowInside == inside) return;

            inside = nowInside;
            Inside.Invoke(inside);
            if (inside) OnEnter.Invoke();
            else OnExit.Invoke();
        }
    }

    public class PlyAreaTrigger : PlyReactiveFeature
    {
        public string requiredTag = "Player";

        public UnityEvent OnEnter = new();
        public UnityEvent OnExit = new();
        public IntEvent OccupancyCount = new();

        private readonly HashSet<GameObject> occupants = new();

        private bool Matches(GameObject obj) => string.IsNullOrWhiteSpace(requiredTag) || obj.CompareTag(requiredTag);

        private void OnTriggerEnter(Collider other)
        {
            if (!enabledFeature || !Matches(other.gameObject)) return;
            occupants.Add(other.gameObject);
            OccupancyCount.Invoke(occupants.Count);
            OnEnter.Invoke();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!enabledFeature || !Matches(other.gameObject)) return;
            occupants.Remove(other.gameObject);
            OccupancyCount.Invoke(occupants.Count);
            OnExit.Invoke();
        }
    }

    // =========================================================
    // REACTIVE METERS / STATE
    // =========================================================

    public class PlyGenericMeter : PlyReactiveFeature
    {
        public float minValue = 0f;
        public float maxValue = 100f;
        public float value = 0f;

        public UnityEvent OnFull = new();
        public UnityEvent OnEmpty = new();
        public FloatEvent OnValueChanged = new();

        private bool wasFull;
        private bool wasEmpty = true;

        public void AddValue(float amount) => SetValue(value + amount);

        public void SetValue(float newValue)
        {
            float previous = value;
            value = Mathf.Clamp(newValue, minValue, maxValue);

            if (!Mathf.Approximately(previous, value))
                OnValueChanged.Invoke(value);

            bool full = value >= maxValue;
            bool empty = value <= minValue;

            if (full && !wasFull) OnFull.Invoke();
            if (empty && !wasEmpty) OnEmpty.Invoke();

            wasFull = full;
            wasEmpty = empty;
        }

        public void Reset() => SetValue(minValue);
    }

    public class PlyDetectionMeter : PlyGenericMeter
    {
        public float increaseRate = 25f;
        public float decreaseRate = 15f;
        public bool increasing;
        public bool decreasing;

        public void SetIncreasing(bool active)
        {
            increasing = active;
            if (active) decreasing = false;
        }

        public void SetDecreasing(bool active)
        {
            decreasing = active;
            if (active) increasing = false;
        }

        private void Update()
        {
            if (!enabledFeature) return;
            if (increasing) AddValue(increaseRate * Time.deltaTime);
            else if (decreasing) AddValue(-decreaseRate * Time.deltaTime);
        }
    }

    public class PlyStateFlag : PlyReactiveFeature
    {
        public string flagName;
        public bool value;

        public UnityEvent OnTrue = new();
        public UnityEvent OnFalse = new();
        public BoolEvent ValueChanged = new();

        public void SetTrue() => SetValue(true);
        public void SetFalse() => SetValue(false);

        public void SetValue(bool newValue)
        {
            if (value == newValue) return;
            value = newValue;
            ValueChanged.Invoke(value);
            if (value) OnTrue.Invoke();
            else OnFalse.Invoke();
        }
    }

    public class PlyStateValue : PlyReactiveFeature
    {
        public string stateName;
        public string initialValue;
        public string value;

        public StringEvent ValueChanged = new();

        private void Awake()
        {
            if (!string.IsNullOrEmpty(initialValue))
                value = initialValue;
        }

        public void SetValue(string newValue)
        {
            value = newValue;
            ValueChanged.Invoke(value);
        }

        public void Reset() => SetValue(initialValue);
    }

    public class PlyQuestState : PlyStateValue {}
    public class PlyDialogState : PlyStateValue {}
    public class PlyTeamAffiliation : PlyStateValue {}

    public class PlyScore : PlyReactiveFeature
    {
        public int value;
        public IntEvent ValueChanged = new();

        public void SetValue(int newValue)
        {
            value = newValue;
            ValueChanged.Invoke(value);
        }

        public void Reset() => SetValue(0);
    }

    public class PlyCurrency : PlyReactiveFeature
    {
        public int value;
        public IntEvent CurrencyChanged = new();

        public void AddCurrency(int amount)
        {
            value += amount;
            CurrencyChanged.Invoke(value);
        }

        public void SpendCurrency(int amount)
        {
            value = Mathf.Max(0, value - amount);
            CurrencyChanged.Invoke(value);
        }
    }

    public class PlyAmmo : PlyReactiveFeature
    {
        public int maxAmmo = 30;
        public int currentAmmo = 30;

        public IntEvent AmmoChanged = new();
        public UnityEvent OnEmpty = new();

        public void ConsumeAmmo(int amount)
        {
            currentAmmo = Mathf.Max(0, currentAmmo - amount);
            AmmoChanged.Invoke(currentAmmo);
            if (currentAmmo <= 0) OnEmpty.Invoke();
        }

        public void AddAmmo(int amount)
        {
            currentAmmo = Mathf.Min(maxAmmo, currentAmmo + amount);
            AmmoChanged.Invoke(currentAmmo);
        }
    }

    public class PlyInventory : PlyReactiveFeature
    {
        public List<GameObject> items = new();
        public UnityEvent InventoryChanged = new();

        public void AddItem(GameObject item)
        {
            if (item != null && !items.Contains(item))
                items.Add(item);
            InventoryChanged.Invoke();
        }

        public void RemoveItem(GameObject item)
        {
            if (item != null)
                items.Remove(item);
            InventoryChanged.Invoke();
        }

        public bool HasItemByName(string itemName)
        {
            return items.Exists(i => i != null && i.name == itemName);
        }
    }

    // =========================================================
    // REACTIVE CONDITIONS / ROUTING
    // =========================================================

    public class PlyStateCompare : PlyReactiveFeature
    {
        public enum CompareOperator { eq, neq, gt, gte, lt, lte }

        public CompareOperator op = CompareOperator.gte;
        public float expectedValue;
        public float value;

        public UnityEvent OnTrue = new();
        public UnityEvent OnFalse = new();
        public BoolEvent Matches = new();

        public void Value(float newValue)
        {
            value = newValue;
            Evaluate();
        }

        public void SetValue(float newValue) => Value(newValue);

        private void Evaluate()
        {
            bool result = op switch
            {
                CompareOperator.eq => Mathf.Approximately(value, expectedValue),
                CompareOperator.neq => !Mathf.Approximately(value, expectedValue),
                CompareOperator.gt => value > expectedValue,
                CompareOperator.gte => value >= expectedValue,
                CompareOperator.lt => value < expectedValue,
                CompareOperator.lte => value <= expectedValue,
                _ => false
            };

            Matches.Invoke(result);
            if (result) OnTrue.Invoke();
            else OnFalse.Invoke();
        }
    }

    public class PlyConditionCheck : PlyReactiveFeature
    {
        public UnityEvent OnTrue = new();
        public UnityEvent OnFalse = new();

        public void Condition(bool value)
        {
            if (value) OnTrue.Invoke();
            else OnFalse.Invoke();
        }

        public void SetCondition(bool value) => Condition(value);
    }

    public class PlyEventRouter : PlyReactiveFeature
    {
        public UnityEvent OutA = new();
        public UnityEvent OutB = new();
        public UnityEvent OutC = new();

        public void In()
        {
            OutA.Invoke();
            OutB.Invoke();
            OutC.Invoke();
        }
    }

    public class PlyCounter : PlyReactiveFeature
    {
        public int initialValue;
        public int targetValue = 1;
        public int value;

        public UnityEvent OnTargetReached = new();
        public IntEvent ValueChanged = new();

        private void Awake() => value = initialValue;

        public void Increment() => Add(1);
        public void Decrement() => Add(-1);

        public void Add(int amount)
        {
            value += amount;
            ValueChanged.Invoke(value);
            if (value >= targetValue) OnTargetReached.Invoke();
        }

        public void Reset()
        {
            value = initialValue;
            ValueChanged.Invoke(value);
        }
    }

    // =========================================================
    // REACTIVE HEALTH / DAMAGE / ALERT
    // =========================================================

    public class PlyHealth : PlyReactiveFeature
    {
        public float maxHealth = 100f;
        public float currentHealth = 100f;

        public FloatEvent HealthChanged = new();
        public UnityEvent OnDepleted = new();

        public void ApplyDamage(float amount) => SetHealth(currentHealth - amount);
        public void ApplyHealing(float amount) => SetHealth(currentHealth + amount);

        public void SetHealth(float value)
        {
            currentHealth = Mathf.Clamp(value, 0f, maxHealth);
            HealthChanged.Invoke(currentHealth);
            if (currentHealth <= 0f) OnDepleted.Invoke();
        }
    }

    public class PlyDamageReceiver : PlyReactiveFeature
    {
        public PlyHealth health;
        public FloatEvent DamageTaken = new();

        private void Reset() => health = GetComponent<PlyHealth>();

        public void ApplyDamage(float amount)
        {
            if (!enabledFeature) return;
            if (health != null) health.ApplyDamage(amount);
            DamageTaken.Invoke(amount);
        }
    }

    public class PlyDeathEvent : PlyReactiveFeature
    {
        public UnityEvent Died = new();
        public void OnDeath() => Died.Invoke();
    }

    public class PlyEnemyAggression : PlyReactiveFeature
    {
        public bool aggressive;
        public UnityEvent OnAggressive = new();
        public BoolEvent AggressionChanged = new();

        public void SetAggressive()
        {
            aggressive = true;
            AggressionChanged.Invoke(true);
            OnAggressive.Invoke();
        }

        public void SetPassive()
        {
            aggressive = false;
            AggressionChanged.Invoke(false);
        }
    }

    public class PlyEnemyAlert : PlyReactiveFeature
    {
        public bool alert;
        public BoolEvent AlertChanged = new();

        public void Alert()
        {
            alert = true;
            AlertChanged.Invoke(true);
        }

        public void ClearAlert()
        {
            alert = false;
            AlertChanged.Invoke(false);
        }
    }

    // Empty marker/reactive placeholders for creator-owned implementations.
    public class PlyVehicleControl : PlyReactiveFeature {}
    public class PlyPlayerControl : PlyReactiveFeature {}

    // =========================================================
    // SYNC FEATURES
    // =========================================================

    public class PlyInteraction : PlySyncFeature
    {
        public string interactionName = "interact";
        public UnityEvent OnInteracted = new();

        public void Trigger()
        {
            if (!enabledFeature) return;
            OnInteracted.Invoke();
            Complete();
        }

        public override void StartAction() => Trigger();
    }

    public class PlyBranch : PlySyncFeature
    {
        public bool condition;
        public UnityEvent True = new();
        public UnityEvent False = new();

        public void Condition(bool value) => condition = value;
        public void Trigger()
        {
            if (!enabledFeature) return;
            if (condition) True.Invoke();
            else False.Invoke();
            Complete();
        }

        public override void StartAction() => Trigger();
    }

    public class PlyRandomChance : PlySyncFeature
    {
        [Range(0f, 1f)] public float probability = 0.5f;
        public UnityEvent Success = new();
        public UnityEvent Failure = new();

        public void Trigger()
        {
            if (!enabledFeature) return;
            if (UnityEngine.Random.value <= probability) Success.Invoke();
            else Failure.Invoke();
            Complete();
        }

        public override void StartAction() => Trigger();
    }

    public class PlyHealing : PlySyncFeature
    {
        public PlyHealth health;
        public UnityEvent Completed = new();

        private void Reset() => health = GetComponent<PlyHealth>();

        public void ApplyHealing(float amount)
        {
            if (!enabledFeature) return;
            if (health != null) health.ApplyHealing(amount);
            Completed.Invoke();
            Complete();
        }
    }

    public class PlyWeaponFire : PlySyncFeature
    {
        public UnityEvent OnFired = new();
        public UnityEvent ProjectileSpawnRequest = new();

        public void Trigger()
        {
            if (!enabledFeature) return;
            OnFired.Invoke();
            ProjectileSpawnRequest.Invoke();
            Complete();
        }

        public override void StartAction() => Trigger();
    }

    public class PlyProjectileSpawner : PlySyncFeature
    {
        public GameObject projectilePrefab;
        public Transform spawnPoint;
        public GameObjectEvent ProjectileSpawned = new();

        private void Reset() => spawnPoint = transform;

        public void SpawnProjectile()
        {
            if (!enabledFeature) return;

            if (projectilePrefab == null)
            {
                Debug.LogWarning($"{nameof(PlyProjectileSpawner)} missing projectilePrefab.", this);
                Complete();
                return;
            }

            Transform point = spawnPoint != null ? spawnPoint : transform;
            GameObject obj = Instantiate(projectilePrefab, point.position, point.rotation);
            ProjectileSpawned.Invoke(obj);
            Complete();
        }

        public override void StartAction() => SpawnProjectile();
    }

    public class PlyPickup : PlySyncFeature
    {
        public bool destroyOnCollect = true;
        public UnityEvent Collected = new();

        public void Trigger()
        {
            if (!enabledFeature) return;
            Collected.Invoke();
            Complete();
            if (destroyOnCollect) Destroy(gameObject);
        }

        public override void StartAction() => Trigger();
    }

    public class PlyCheckpoint : PlySyncFeature
    {
        public static Vector3 LastCheckpointPosition;
        public UnityEvent CheckpointReached = new();

        public void Trigger()
        {
            if (!enabledFeature) return;
            LastCheckpointPosition = transform.position;
            CheckpointReached.Invoke();
            Complete();
        }

        public override void StartAction() => Trigger();
    }

    public class PlySpawnPoint : PlySyncFeature
    {
        public GameObject prefab;
        public Transform spawnPoint;
        public GameObjectEvent Spawned = new();

        private void Reset() => spawnPoint = transform;

        public void Trigger()
        {
            if (!enabledFeature) return;

            Transform point = spawnPoint != null ? spawnPoint : transform;
            GameObject obj = prefab != null
                ? Instantiate(prefab, point.position, point.rotation)
                : null;

            Spawned.Invoke(obj);
            Complete();
        }

        public override void StartAction() => Trigger();
    }

    public class PlySpawnWave : PlySyncFeature
    {
        public GameObject prefab;
        public Transform spawnPoint;
        public int count = 5;
        public float spacing = 1.5f;
        public UnityEvent WaveSpawned = new();

        private void Reset() => spawnPoint = transform;

        public void Trigger()
        {
            if (!enabledFeature) return;

            Transform point = spawnPoint != null ? spawnPoint : transform;

            if (prefab != null)
            {
                for (int i = 0; i < count; i++)
                    Instantiate(prefab, point.position + point.right * spacing * i, point.rotation);
            }

            WaveSpawned.Invoke();
            Complete();
        }

        public override void StartAction() => Trigger();
    }

    public class PlyAnimationTrigger : PlySyncFeature
    {
        public Animator animator;
        public string animationName;
        public UnityEvent AnimationTriggered = new();

        private void Reset() => animator = GetComponent<Animator>();

        public void Play()
        {
            if (!enabledFeature) return;

            if (animator != null && !string.IsNullOrWhiteSpace(animationName))
                animator.SetTrigger(animationName);

            AnimationTriggered.Invoke();
            Complete();
        }

        public override void StartAction() => Play();
    }

    public class PlyOpenBehavior : PlySyncFeature
    {
        public Animator animator;
        public string openTrigger = "Open";
        public UnityEvent Opened = new();

        private void Reset() => animator = GetComponent<Animator>();

        public void Trigger()
        {
            if (!enabledFeature) return;
            if (animator != null && !string.IsNullOrEmpty(openTrigger))
                animator.SetTrigger(openTrigger);
            Opened.Invoke();
            Complete();
        }

        public override void StartAction() => Trigger();
    }

    public class PlyInventoryHasItem : PlySyncFeature
    {
        public PlyInventory inventory;
        public string itemName;

        public UnityEvent OnTrue = new();
        public UnityEvent OnFalse = new();
        public BoolEvent HasItem = new();

        private void Reset() => inventory = GetComponent<PlyInventory>();

        public void Check()
        {
            if (!enabledFeature) return;
            bool has = inventory != null && inventory.HasItemByName(itemName);
            HasItem.Invoke(has);
            if (has) OnTrue.Invoke();
            else OnFalse.Invoke();
            Complete();
        }

        public override void StartAction() => Check();
    }

    public class PlySoundEvent : PlySyncFeature
    {
        public AudioSource audioSource;
        public AudioClip clip;
        public UnityEvent Played = new();

        private void Reset() => audioSource = GetComponent<AudioSource>();

        public void Play()
        {
            if (!enabledFeature) return;

            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            if (clip != null) audioSource.PlayOneShot(clip);
            else audioSource.Play();

            Played.Invoke();
            Complete();
        }

        public override void StartAction() => Play();
    }

    // =========================================================
    // ASYNC FLOW / CONTROL
    // =========================================================

    public class PlyGate : PlyAsyncFeature
    {
        public bool startsOpen = true;
        public bool isOpen;
        public UnityEvent Passed = new();

        private void Awake() => isOpen = startsOpen;

        public void SetOpen(bool value) => isOpen = value;

        public void Enter()
        {
            StartAction();
            if (!running) return;
            if (isOpen) Passed.Invoke();
            Complete();
        }
    }

    public class PlyCooldown : PlyAsyncFeature
    {
        public float cooldownSeconds = 1f;
        public UnityEvent OnReady = new();
        public UnityEvent OnBlocked = new();

        private float lastReadyTime = -99999f;

        public void Trigger()
        {
            StartAction();
            if (!running) return;

            if (Time.time >= lastReadyTime + cooldownSeconds)
            {
                lastReadyTime = Time.time;
                OnReady.Invoke();
            }
            else
            {
                OnBlocked.Invoke();
            }

            Complete();
        }
    }

    public class PlyTimerDelay : PlyAsyncFeature
    {
        public float delaySeconds = 1f;
        public UnityEvent AfterDelay = new();

        public void Trigger()
        {
            StartAction();
            if (!running) return;
            runningRoutine = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            float t = 0f;
            while (running && t < delaySeconds)
            {
                yield return WaitWhilePaused();
                t += Time.deltaTime;
                yield return null;
            }

            if (!running) yield break;
            AfterDelay.Invoke();
            Complete();
        }
    }

    public class PlySequence : PlyAsyncFeature
    {
        public List<PlyAsyncFeature> steps = new();
        public UnityEvent Step1 = new();
        public UnityEvent Step2 = new();
        public UnityEvent Completed = new();

        private int index;

        public void Start()
        {
            StartAction();
            if (!running) return;
            index = 0;
            RunCurrent();
        }

        public override void CancelAction()
        {
            if (index >= 0 && index < steps.Count && steps[index] != null)
                steps[index].CancelAction();
            base.CancelAction();
        }

        private void RunCurrent()
        {
            if (!running) return;

            if (index >= steps.Count)
            {
                Completed.Invoke();
                Complete();
                return;
            }

            if (index == 0) Step1.Invoke();
            if (index == 1) Step2.Invoke();

            var step = steps[index];

            if (step == null)
            {
                index++;
                RunCurrent();
                return;
            }

            step.OnCompleted.AddListener(HandleStepCompleted);
            step.OnFailed.AddListener(HandleStepFailed);
            step.StartAction();
        }

        private void HandleStepCompleted()
        {
            var step = steps[index];
            step.OnCompleted.RemoveListener(HandleStepCompleted);
            step.OnFailed.RemoveListener(HandleStepFailed);

            index++;
            RunCurrent();
        }

        private void HandleStepFailed(string reason)
        {
            var step = steps[index];
            step.OnCompleted.RemoveListener(HandleStepCompleted);
            step.OnFailed.RemoveListener(HandleStepFailed);
            Fail(reason);
        }
    }

    public class PlyRepeatUntil : PlyAsyncFeature
    {
        public bool stopCondition;
        public float intervalSeconds = 0f;
        public UnityEvent Tick = new();
        public UnityEvent Completed = new();

        public void SetStopCondition(bool value) => stopCondition = value;

        public void Start()
        {
            StartAction();
            if (!running) return;
            runningRoutine = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            while (running && !stopCondition)
            {
                yield return WaitWhilePaused();
                Tick.Invoke();

                if (intervalSeconds > 0f)
                    yield return new WaitForSeconds(intervalSeconds);
                else
                    yield return null;
            }

            if (!running) yield break;
            Completed.Invoke();
            Complete();
        }
    }

    public class PlyParallel : PlyAsyncFeature
    {
        public List<PlyAsyncFeature> children = new();
        public StringEvent ChildCompleted = new();

        private int remaining;

        public override void StartAction()
        {
            base.StartAction();
            if (!running) return;

            remaining = 0;

            foreach (var child in children)
            {
                if (child == null) continue;
                remaining++;
                child.OnCompleted.AddListener(() => HandleChildCompleted(child.name));
                child.OnFailed.AddListener(HandleChildFailed);
                child.StartAction();
            }

            if (remaining == 0) Complete();
        }

        private void HandleChildCompleted(string childName)
        {
            ChildCompleted.Invoke(childName);
            remaining--;
            if (remaining <= 0) Complete();
        }

        private void HandleChildFailed(string reason) => Fail(reason);

        public override void CancelAction()
        {
            foreach (var child in children)
                if (child != null) child.CancelAction();
            base.CancelAction();
        }
    }

    public class PlySelector : PlyAsyncFeature
    {
        public List<PlyAsyncFeature> children = new();
        public StringEvent ChildSelected = new();

        private int index;

        public override void StartAction()
        {
            base.StartAction();
            if (!running) return;
            index = 0;
            TryCurrent();
        }

        private void TryCurrent()
        {
            if (!running) return;

            if (index >= children.Count)
            {
                Fail("Selector failed: no child succeeded.");
                return;
            }

            var child = children[index];
            if (child == null)
            {
                index++;
                TryCurrent();
                return;
            }

            ChildSelected.Invoke(child.name);
            child.OnCompleted.AddListener(HandleChildCompleted);
            child.OnFailed.AddListener(HandleChildFailed);
            child.StartAction();
        }

        private void HandleChildCompleted()
        {
            var child = children[index];
            child.OnCompleted.RemoveListener(HandleChildCompleted);
            child.OnFailed.RemoveListener(HandleChildFailed);
            Complete();
        }

        private void HandleChildFailed(string _)
        {
            var child = children[index];
            child.OnCompleted.RemoveListener(HandleChildCompleted);
            child.OnFailed.RemoveListener(HandleChildFailed);
            index++;
            TryCurrent();
        }
    }

    public class PlyLoop : PlyAsyncFeature
    {
        public float intervalSeconds = 0f;
        public UnityEvent Iteration = new();

        public override void StartAction()
        {
            base.StartAction();
            if (!running) return;
            runningRoutine = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            while (running)
            {
                yield return WaitWhilePaused();
                Iteration.Invoke();

                if (intervalSeconds > 0f)
                    yield return new WaitForSeconds(intervalSeconds);
                else
                    yield return null;
            }
        }
    }

    public class PlyWaitUntil : PlyAsyncFeature
    {
        public bool condition;
        public UnityEvent ConditionMet = new();

        public void Condition(bool value) => condition = value;
        public void SetCondition(bool value) => condition = value;

        public override void StartAction()
        {
            base.StartAction();
            if (!running) return;
            runningRoutine = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            while (running && !condition)
            {
                yield return WaitWhilePaused();
                yield return null;
            }

            if (!running) yield break;
            ConditionMet.Invoke();
            Complete();
        }
    }

    public class PlyWait : PlyAsyncFeature
    {
        public float durationSeconds = 1f;
        public UnityEvent Completed = new();

        public void Trigger()
        {
            StartAction();
            if (!running) return;
            runningRoutine = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            float t = 0f;
            while (running && t < durationSeconds)
            {
                yield return WaitWhilePaused();
                t += Time.deltaTime;
                yield return null;
            }

            if (!running) yield break;
            Completed.Invoke();
            Complete();
        }
    }

    // =========================================================
    // ASYNC MOVEMENT / BEHAVIOR
    // =========================================================

    public class PlyPathfindingMoveTo : PlyAsyncFeature
    {
        public NavMeshAgent agent;
        public float arrivalDistance = 0.25f;
        public UnityEvent Arrived = new();

        private Vector3 destination;
        private bool hasDestination;

        private void Reset() => agent = GetComponent<NavMeshAgent>();

        public void MoveTo(Vector3 position)
        {
            destination = position;
            hasDestination = true;

            if (running && agent != null)
                agent.SetDestination(destination);
        }

        public override void StartAction()
        {
            base.StartAction();
            if (!running) return;

            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (agent == null)
            {
                Fail("NavMeshAgent missing.");
                return;
            }

            if (!hasDestination)
            {
                Fail("Destination not set.");
                return;
            }

            agent.isStopped = false;
            agent.SetDestination(destination);
            runningRoutine = StartCoroutine(WaitForArrival());
        }

        private IEnumerator WaitForArrival()
        {
            while (running)
            {
                yield return WaitWhilePaused();

                if (paused)
                {
                    if (agent != null) agent.isStopped = true;
                    yield return null;
                    continue;
                }

                if (agent != null) agent.isStopped = false;

                if (!agent.pathPending && agent.remainingDistance <= Mathf.Max(arrivalDistance, agent.stoppingDistance))
                {
                    Arrived.Invoke();
                    Complete();
                    yield break;
                }

                yield return null;
            }
        }

        public override void CancelAction()
        {
            if (agent != null) agent.isStopped = true;
            base.CancelAction();
        }
    }

    public class PlyMoveToTarget : PlyPathfindingMoveTo
    {
        public Transform assignedTarget;
        public float speed = 3.5f;

        public void AssignTarget(GameObject target)
        {
            assignedTarget = target != null ? target.transform : null;
            if (assignedTarget != null)
                MoveTo(assignedTarget.position);
        }

        public void SetDestination(Vector3 destination) => MoveTo(destination);

        public override void StartAction()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (agent != null) agent.speed = speed;
            if (assignedTarget != null) MoveTo(assignedTarget.position);
            base.StartAction();
        }
    }

    public class PlySearchLastKnownPosition : PlyPathfindingMoveTo
    {
        public UnityEvent SearchStarted = new();

        public void Search(Vector3 position)
        {
            MoveTo(position);
            SearchStarted.Invoke();
            StartAction();
        }
    }

    public class PlyPlayAnimation : PlyAsyncFeature
    {
        public Animator animator;
        public string animationName;
        public UnityEvent AnimationFinished = new();

        private void Reset() => animator = GetComponent<Animator>();

        public void Trigger()
        {
            StartAction();
            if (!running) return;

            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null || string.IsNullOrWhiteSpace(animationName))
            {
                Fail("Animator or animationName missing.");
                return;
            }

            animator.Play(animationName);
            runningRoutine = StartCoroutine(WaitForAnimation());
        }

        private IEnumerator WaitForAnimation()
        {
            yield return null;

            while (running && animator != null)
            {
                yield return WaitWhilePaused();

                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.normalizedTime >= 1f && !animator.IsInTransition(0))
                    break;

                yield return null;
            }

            if (!running) yield break;
            AnimationFinished.Invoke();
            Complete();
        }
    }

    // These are async semantic placeholders. They can be connected to creator wrappers or configured via UnityEvents.
    public class PlySemanticAsyncAction : PlyAsyncFeature
    {
        public UnityEvent Triggered = new();
        public UnityEvent Completed = new();

        public void Trigger()
        {
            StartAction();
            if (!running) return;
            Triggered.Invoke();
            // By default, semantic placeholders complete immediately.
            // Third-party wrappers can replace/extend these when real behavior is available.
            Completed.Invoke();
            Complete();
        }
    }

    public class PlyHideBehavior : PlySemanticAsyncAction { public UnityEvent Hidden => Completed; }
    public class PlyTalkToNpc : PlySemanticAsyncAction { public UnityEvent DialogFinished => Completed; }
    public class PlyAttackTarget : PlySemanticAsyncAction {}
    public class PlyPatrol : PlySemanticAsyncAction {}
    public class PlyFlee : PlySemanticAsyncAction {}
    public class PlySearchArea : PlySemanticAsyncAction {}

    // =========================================================
    // UNITY BRIDGES
    // =========================================================

    public class PlyUnityTriggerBridge : PlyReactiveFeature
    {
        public string requiredTag = "Player";

        public GameObjectEvent OnTriggerEnterEvent = new();
        public GameObjectEvent OnTriggerExitEvent = new();
        public GameObjectEvent OnTriggerStayEvent = new();

        private bool Matches(GameObject obj) => string.IsNullOrWhiteSpace(requiredTag) || obj.CompareTag(requiredTag);

        private void OnTriggerEnter(Collider other)
        {
            if (enabledFeature && Matches(other.gameObject)) OnTriggerEnterEvent.Invoke(other.gameObject);
        }

        private void OnTriggerExit(Collider other)
        {
            if (enabledFeature && Matches(other.gameObject)) OnTriggerExitEvent.Invoke(other.gameObject);
        }

        private void OnTriggerStay(Collider other)
        {
            if (enabledFeature && Matches(other.gameObject)) OnTriggerStayEvent.Invoke(other.gameObject);
        }
    }

    public class PlyUnityCollisionBridge : PlyReactiveFeature
    {
        public string requiredTag = "Player";

        public GameObjectEvent OnCollisionEnterEvent = new();
        public GameObjectEvent OnCollisionExitEvent = new();

        private bool Matches(GameObject obj) => string.IsNullOrWhiteSpace(requiredTag) || obj.CompareTag(requiredTag);

        private void OnCollisionEnter(Collision collision)
        {
            if (enabledFeature && Matches(collision.gameObject)) OnCollisionEnterEvent.Invoke(collision.gameObject);
        }

        private void OnCollisionExit(Collision collision)
        {
            if (enabledFeature && Matches(collision.gameObject)) OnCollisionExitEvent.Invoke(collision.gameObject);
        }
    }

    public class PlyUnityLifecycleBridge : PlyReactiveFeature
    {
        public UnityEvent StartEvent = new();
        public UnityEvent OnEnableEvent = new();
        public UnityEvent OnDisableEvent = new();

        private void Start()
        {
            if (enabledFeature) StartEvent.Invoke();
        }

        private void OnEnable()
        {
            if (enabledFeature) OnEnableEvent.Invoke();
        }

        private void OnDisable()
        {
            OnDisableEvent.Invoke();
        }
    }

    public class PlyUnityVisibilityBridge : PlyReactiveFeature
    {
        public UnityEvent OnBecameVisibleEvent = new();
        public UnityEvent OnBecameInvisibleEvent = new();

        private void OnBecameVisible()
        {
            if (enabledFeature) OnBecameVisibleEvent.Invoke();
        }

        private void OnBecameInvisible()
        {
            if (enabledFeature) OnBecameInvisibleEvent.Invoke();
        }
    }

    public class PlyUnityInputBridge : PlyReactiveFeature
    {
        public string inputName = "Fire1";
        public UnityEvent OnPressed = new();
        public UnityEvent OnReleased = new();

        private void Update()
        {
            if (!enabledFeature) return;
            if (Input.GetButtonDown(inputName)) OnPressed.Invoke();
            if (Input.GetButtonUp(inputName)) OnReleased.Invoke();
        }
    }

    public class PlyUnityPointerBridge : PlyReactiveFeature, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public UnityEvent OnPointerClickEvent = new();
        public UnityEvent OnPointerEnterEvent = new();
        public UnityEvent OnPointerExitEvent = new();

        public void OnPointerClick(PointerEventData eventData)
        {
            if (enabledFeature) OnPointerClickEvent.Invoke();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (enabledFeature) OnPointerEnterEvent.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (enabledFeature) OnPointerExitEvent.Invoke();
        }
    }

    public class PlyUnityAnimationBridge : PlyReactiveFeature
    {
        public StringEvent OnAnimationEvent = new();

        public void AnimationEvent(string eventName)
        {
            if (enabledFeature) OnAnimationEvent.Invoke(eventName);
        }
    }

    public class PlyUnityUpdateTickBridge : PlyReactiveFeature
    {
        public UnityEvent UpdateEvent = new();
        public UnityEvent FixedUpdateEvent = new();

        private void Update()
        {
            if (enabledFeature) UpdateEvent.Invoke();
        }

        private void FixedUpdate()
        {
            if (enabledFeature) FixedUpdateEvent.Invoke();
        }
    }

    public class PlyUnityTimerBridge : PlyAsyncFeature
    {
        public float intervalSeconds = 1f;
        public UnityEvent OnTick = new();
        public FloatEvent ElapsedSeconds = new();

        private float elapsed;
        private float timer;

        public override void StartAction()
        {
            base.StartAction();
            if (!running) return;
            runningRoutine = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            elapsed = 0f;
            timer = 0f;

            while (running)
            {
                yield return WaitWhilePaused();

                elapsed += Time.deltaTime;
                timer += Time.deltaTime;

                ElapsedSeconds.Invoke(elapsed);

                if (timer >= intervalSeconds)
                {
                    timer = 0f;
                    OnTick.Invoke();
                }

                yield return null;
            }
        }
    }

    // =========================================================
    // UI UPDATE
    // =========================================================

    public class PlyUiUpdate : PlyReactiveFeature
    {
        public Text legacyText;
        public Slider slider;
        public UnityEvent Updated = new();

        public void Value(float value)
        {
            if (slider != null) slider.value = value;
            if (legacyText != null) legacyText.text = value.ToString("0.##");
            Updated.Invoke();
        }

        public void Value(string value)
        {
            if (legacyText != null) legacyText.text = value;
            Updated.Invoke();
        }

        public void Value(object value)
        {
            if (legacyText != null) legacyText.text = value?.ToString() ?? string.Empty;
            Updated.Invoke();
        }
    }
}
