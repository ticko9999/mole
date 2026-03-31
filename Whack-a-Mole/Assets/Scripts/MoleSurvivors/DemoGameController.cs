using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MoleSurvivors
{
    public static class DemoBootstrapper
    {
        public static bool DisableAutoBootstrapForTests;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Application.isPlaying || DisableAutoBootstrapForTests)
            {
                return;
            }

            if (UnityEngine.Object.FindObjectOfType<DemoGameController>() != null)
            {
                return;
            }

            GameObject root = new GameObject("MoleSurvivorsDemo");
            root.AddComponent<DemoGameController>();
        }
    }

    public sealed class MoleRuntime
    {
        public MoleRuntime(MoleDef def, float hpScale, bool canSplitOnDeath = true, float spawnedAtSecond = 0f)
        {
            Def = def;
            RemainingHp = def.BaseHp * hpScale;
            ShieldHp = def.Traits.HasFlag(MoleTrait.Shield) ? Mathf.Max(6f, Def.BaseHp * 0.35f) : 0f;
            CanSplitOnDeath = canSplitOnDeath && def.Traits.HasFlag(MoleTrait.Split);
            SpawnedAtSecond = Mathf.Max(0f, spawnedAtSecond);
        }

        public MoleDef Def { get; }

        public float RemainingHp { get; private set; }

        public float ShieldHp { get; private set; }

        public bool CanSplitOnDeath { get; }

        public float SpawnedAtSecond { get; }

        public int TotalHitCount { get; private set; }

        public int ManualHitCount { get; private set; }

        public void RegisterHit(AttackSource source)
        {
            TotalHitCount++;
            if (source == AttackSource.Manual)
            {
                ManualHitCount++;
            }
        }

        public DamageResult ApplyDamage(float amount)
        {
            bool shieldBroken = false;
            float clampedDamage = Mathf.Max(0f, amount);
            if (ShieldHp > 0f)
            {
                ShieldHp -= clampedDamage;
                if (ShieldHp > 0f)
                {
                    return new DamageResult(false, false, RemainingHp);
                }

                shieldBroken = true;
                clampedDamage = -ShieldHp;
                ShieldHp = 0f;
            }

            RemainingHp -= clampedDamage;
            bool killed = RemainingHp <= 0f;
            return new DamageResult(killed, shieldBroken, RemainingHp);
        }
    }

    public sealed class HoleRuntime
    {
        private const float StateBlendDuration = 0.08f;
        private readonly SpriteRenderer _holeRenderer;
        private readonly SpriteRenderer _moleRenderer;
        private readonly TextMesh _hpText;
        private readonly TextMesh _facilityText;
        private readonly TextMesh _lockText;
        private readonly PresentationSkin _presentationSkin;
        private readonly Dictionary<string, MoleVisualEntry> _moleVisualLookup;
        private float _timer;
        private float _timingScale = 1f;
        private float _stateBlendTimer;
        private bool _hasVisualState;
        private HoleState _lastVisualState;
        private bool _eventPressureActive;
        private MoleDef _lastMoleDef;
        private bool _retreatAfterHitFlash;
        private readonly bool _hasHoleArtSprite;

        public HoleRuntime(
            int index,
            Vector2 position,
            float spawnWeight,
            int dangerLevel,
            SpriteRenderer holeRenderer,
            SpriteRenderer moleRenderer,
            TextMesh hpText,
            TextMesh facilityText = null,
            TextMesh lockText = null,
            PresentationSkin presentationSkin = null,
            Dictionary<string, MoleVisualEntry> moleVisualLookup = null)
        {
            Index = index;
            Position = position;
            SpawnWeight = spawnWeight;
            DangerLevel = dangerLevel;
            _holeRenderer = holeRenderer;
            _moleRenderer = moleRenderer;
            _hpText = hpText;
            _facilityText = facilityText;
            _lockText = lockText;
            _presentationSkin = presentationSkin;
            _moleVisualLookup = moleVisualLookup;
            _hasHoleArtSprite = _holeRenderer != null &&
                _holeRenderer.sprite != null &&
                _holeRenderer.sprite != SpriteCache.HoleFallbackSprite &&
                _holeRenderer.sprite != SpriteCache.PlaceholderBlockSprite;
            FitHoleScale(0.56f, 1.24f);
            State = HoleState.Idle;
            RefreshVisual();
        }

        public int Index { get; }

        public Vector2 Position { get; }

        public float SpawnWeight { get; }

        public int DangerLevel { get; }

        public HoleState State { get; private set; }

        public MoleRuntime CurrentMole { get; private set; }

        public FacilityRuntime Facility { get; private set; }

        public bool IsActive { get; private set; } = true;

        public bool IsLocked => !IsActive;

        public bool CanInstallFacility => IsActive && Facility == null;

        public float RareWeightMultiplier { get; private set; } = 1f;

        public float GoldRewardMultiplier { get; private set; } = 1f;

        public float LocalMagnetRadius { get; private set; }

        public bool CanSpawn => IsActive && State == HoleState.Idle && CurrentMole == null;

        public bool HasLiveMole => IsActive && CurrentMole != null && State == HoleState.HitWindow;

        public bool IsTargetable => IsActive && CurrentMole != null && (State == HoleState.HitWindow || State == HoleState.HitFlash);

        public bool EventPressureActive => _eventPressureActive;

        public bool VisualContainsPoint(Vector2 worldPoint, float padding = 0.06f)
        {
            if (!IsTargetable || _moleRenderer == null || !_moleRenderer.gameObject.activeInHierarchy)
            {
                return false;
            }

            Bounds bounds = _moleRenderer.bounds;
            bounds.Expand(new Vector3(Mathf.Max(0f, padding), Mathf.Max(0f, padding), 0f));
            return bounds.Contains(new Vector3(worldPoint.x, worldPoint.y, bounds.center.z));
        }

        public float DistanceToVisualCenter(Vector2 worldPoint)
        {
            if (_moleRenderer == null)
            {
                return Vector2.Distance(Position, worldPoint);
            }

            Vector3 center = _moleRenderer.bounds.center;
            return Vector2.Distance(new Vector2(center.x, center.y), worldPoint);
        }

        public void InstallFacility(FacilityRuntime facility)
        {
            if (!IsActive || facility == null)
            {
                return;
            }

            Facility = facility;
            RefreshFacilityLabel();
            RefreshVisual();
        }

        public void ClearFacility()
        {
            Facility = null;
            RareWeightMultiplier = 1f;
            GoldRewardMultiplier = 1f;
            LocalMagnetRadius = 0f;
            if (_facilityText != null)
            {
                _facilityText.gameObject.SetActive(false);
                _facilityText.text = string.Empty;
            }

            RefreshVisual();
        }

        public void ApplyFacilityPassives(float rareWeightMultiplier, float goldRewardMultiplier, float localMagnetRadius)
        {
            if (!IsActive)
            {
                RareWeightMultiplier = 1f;
                GoldRewardMultiplier = 1f;
                LocalMagnetRadius = 0f;
                return;
            }

            RareWeightMultiplier = Mathf.Max(1f, rareWeightMultiplier);
            GoldRewardMultiplier = Mathf.Max(1f, goldRewardMultiplier);
            LocalMagnetRadius = Mathf.Max(0f, localMagnetRadius);
            RefreshFacilityLabel();
        }

        public void Spawn(
            MoleDef def,
            float hpScale,
            float timingScale = 1f,
            bool canSplitOnDeath = true,
            float spawnedAtSecond = 0f)
        {
            if (!IsActive)
            {
                return;
            }

            CurrentMole = new MoleRuntime(def, hpScale, canSplitOnDeath, spawnedAtSecond);
            _lastMoleDef = def;
            _timingScale = Mathf.Clamp(timingScale, 0.85f, 2f);
            State = HoleState.Warning;
            _timer = Mathf.Max(0.12f, def.WarningSeconds * _timingScale);
            RefreshVisual();
        }

        public void SetActive(bool active)
        {
            if (IsActive == active)
            {
                return;
            }

            IsActive = active;
            if (!IsActive)
            {
                CurrentMole = null;
                _lastMoleDef = null;
                State = HoleState.Idle;
                _timer = 0f;
                ClearFacility();
                RareWeightMultiplier = 1f;
                GoldRewardMultiplier = 1f;
                LocalMagnetRadius = 0f;
            }
            else
            {
                State = HoleState.Idle;
                _timer = 0f;
            }

            RefreshVisual();
        }

        public void Tick(float deltaTime, Action<HoleRuntime> escapeCallback)
        {
            if (!IsActive)
            {
                if (_hpText != null)
                {
                    _hpText.gameObject.SetActive(false);
                }

                if (_facilityText != null)
                {
                    _facilityText.gameObject.SetActive(false);
                }

                return;
            }

            _timer -= deltaTime;
            switch (State)
            {
                case HoleState.Warning:
                    if (_timer <= 0f)
                    {
                        State = HoleState.HitWindow;
                        _timer = Mathf.Max(0.22f, CurrentMole.Def.UpSeconds * _timingScale);
                        RefreshVisual();
                    }

                    break;
                case HoleState.HitWindow:
                    if (_timer <= 0f)
                    {
                        escapeCallback?.Invoke(this);
                    }

                    break;
                case HoleState.HitFlash:
                    if (_timer <= 0f)
                    {
                        if (_retreatAfterHitFlash)
                        {
                            _retreatAfterHitFlash = false;
                            EnterRetreat(_lastMoleDef, 0.07f, 0.08f);
                            break;
                        }

                        if (CurrentMole != null)
                        {
                            State = HoleState.HitWindow;
                            _timer = Mathf.Max(0.1f, 0.16f * _timingScale);
                        }
                        else
                        {
                            BeginCooldown(0.2f);
                        }

                        RefreshVisual();
                    }

                    break;
                case HoleState.Retreat:
                    if (_timer <= 0f)
                    {
                        BeginCooldown(0.24f);
                    }

                    break;
                case HoleState.Cooldown:
                    if (_timer <= 0f)
                    {
                        State = HoleState.Idle;
                        _lastMoleDef = null;
                        RefreshVisual();
                    }

                    break;
                case HoleState.OccupiedByEvent:
                    if (_timer <= 0f)
                    {
                        State = HoleState.Idle;
                        _lastMoleDef = null;
                        RefreshVisual();
                    }

                    break;
            }

            if (_stateBlendTimer > 0f)
            {
                _stateBlendTimer = Mathf.Max(0f, _stateBlendTimer - deltaTime);
                if (_moleRenderer != null && _moleRenderer.gameObject.activeSelf)
                {
                    RefreshVisual();
                }
            }

            if (_hpText != null)
            {
                if (CurrentMole != null && (State == HoleState.HitWindow || State == HoleState.HitFlash))
                {
                    _hpText.gameObject.SetActive(true);
                    _hpText.text = Mathf.CeilToInt(Mathf.Max(0f, CurrentMole.RemainingHp)).ToString();
                }
                else
                {
                    _hpText.gameObject.SetActive(false);
                }
            }

            if (Facility != null)
            {
                Facility.LastHoleHadTarget = HasLiveMole;
                RefreshFacilityLabel();
            }
        }

        public void RegisterHitFlash()
        {
            if (CurrentMole == null)
            {
                return;
            }

            State = HoleState.HitFlash;
            _timer = 0.1f;
            RefreshVisual();
        }

        public void RegisterHitFlash(bool killed)
        {
            if (!killed)
            {
                RegisterHitFlash();
                return;
            }

            MoleDef def = CurrentMole != null ? CurrentMole.Def : _lastMoleDef;
            _lastMoleDef = def ?? _lastMoleDef;
            CurrentMole = null;
            _retreatAfterHitFlash = true;
            State = HoleState.HitFlash;
            _timer = 0.12f;
            RefreshVisual();
        }

        public void KillAndRetreat()
        {
            MoleDef def = CurrentMole != null ? CurrentMole.Def : null;
            EnterRetreat(def, 0.05f, 0.08f);
        }

        public void EscapeAndRetreat()
        {
            MoleDef def = CurrentMole != null ? CurrentMole.Def : null;
            EnterRetreat(def, 0.08f, 0.1f);
        }

        private void EnterRetreat(MoleDef def, float baseSeconds, float cooldownScale)
        {
            _lastMoleDef = def ?? _lastMoleDef;
            CurrentMole = null;
            State = HoleState.Retreat;
            _retreatAfterHitFlash = false;
            _timer = baseSeconds;
            if (def != null)
            {
                _timer += def.CooldownSeconds * cooldownScale;
            }

            RefreshVisual();
        }

        public void OccupyByEvent(float seconds)
        {
            CurrentMole = null;
            _lastMoleDef = null;
            State = HoleState.OccupiedByEvent;
            _timer = Mathf.Max(0.5f, seconds);
            RefreshVisual();
        }

        public void ResetToIdle()
        {
            CurrentMole = null;
            _lastMoleDef = null;
            State = HoleState.Idle;
            _timer = 0f;
            if (Facility != null)
            {
                Facility.LastHoleHadTarget = false;
            }

            RefreshVisual();
        }

        public void SetEventPressure(bool active)
        {
            if (_eventPressureActive == active)
            {
                return;
            }

            _eventPressureActive = active;
            RefreshVisual();
        }

        private void BeginCooldown(float fallbackSeconds)
        {
            State = HoleState.Cooldown;
            float cd = fallbackSeconds;
            if (CurrentMole != null)
            {
                float cooldownScale = Mathf.Clamp(0.9f + (_timingScale - 1f) * 0.42f, 0.85f, 1.45f);
                cd = Mathf.Max(cd, CurrentMole.Def.CooldownSeconds * cooldownScale);
                _lastMoleDef = CurrentMole.Def;
            }

            _timer = cd;
            CurrentMole = null;
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            if (_holeRenderer == null || _moleRenderer == null)
            {
                return;
            }

            if (!IsActive)
            {
                _holeRenderer.color = new Color(0.42f, 0.46f, 0.5f, 0.92f);
                _moleRenderer.gameObject.SetActive(false);
                if (_hpText != null)
                {
                    _hpText.gameObject.SetActive(false);
                }

                if (_facilityText != null)
                {
                    _facilityText.gameObject.SetActive(false);
                    _facilityText.text = string.Empty;
                }

                if (_lockText != null)
                {
                    _lockText.gameObject.SetActive(true);
                    _lockText.text = "锁定";
                    _lockText.color = new Color(0.96f, 0.9f, 0.62f, 0.9f);
                }

                return;
            }

            if (_lockText != null)
            {
                _lockText.gameObject.SetActive(false);
                _lockText.text = string.Empty;
            }

            if (!_hasVisualState)
            {
                _lastVisualState = State;
                _hasVisualState = true;
            }
            else if (_lastVisualState != State)
            {
                _lastVisualState = State;
                _stateBlendTimer = StateBlendDuration;
            }

            switch (State)
            {
                case HoleState.Idle:
                    _holeRenderer.color = ResolveHoleColor(HoleState.Idle);
                    _moleRenderer.gameObject.SetActive(false);
                    break;
                case HoleState.Warning:
                    _holeRenderer.color = ResolveHoleColor(HoleState.Warning);
                    _moleRenderer.gameObject.SetActive(true);
                    _moleRenderer.sprite = ResolveCurrentMoleSprite(HoleState.Warning);
                    _moleRenderer.color = new Color(1f, 0.75f, 0.32f);
                    FitMoleScale(0.34f);
                    break;
                case HoleState.HitWindow:
                    _holeRenderer.color = ResolveHoleColor(HoleState.HitWindow);
                    _moleRenderer.gameObject.SetActive(true);
                    _moleRenderer.sprite = ResolveCurrentMoleSprite(HoleState.HitWindow);
                    _moleRenderer.color = ResolveCurrentMoleTint();
                    FitMoleScale(0.58f);
                    break;
                case HoleState.HitFlash:
                    _holeRenderer.color = ResolveHoleColor(HoleState.HitFlash);
                    _moleRenderer.gameObject.SetActive(true);
                    _moleRenderer.sprite = ResolveCurrentMoleSprite(HoleState.HitFlash);
                    _moleRenderer.color = new Color(1f, 1f, 1f);
                    FitMoleScale(0.62f);
                    break;
                case HoleState.Retreat:
                    _holeRenderer.color = ResolveHoleColor(HoleState.Retreat);
                    _moleRenderer.gameObject.SetActive(CurrentMole != null || _lastMoleDef != null);
                    _moleRenderer.sprite = ResolveCurrentMoleSprite(HoleState.Retreat);
                    _moleRenderer.color = Color.Lerp(ResolveCurrentMoleTint(), new Color(1f, 1f, 1f, 0.88f), 0.42f);
                    FitMoleScale(0.46f);
                    break;
                case HoleState.Cooldown:
                    _holeRenderer.color = ResolveHoleColor(HoleState.Cooldown);
                    _moleRenderer.gameObject.SetActive(CurrentMole != null || _lastMoleDef != null);
                    _moleRenderer.sprite = ResolveCurrentMoleSprite(HoleState.Cooldown);
                    _moleRenderer.color = new Color(1f, 1f, 1f, 0.55f);
                    FitMoleScale(0.4f);
                    break;
                case HoleState.OccupiedByEvent:
                    _holeRenderer.color = ResolveHoleColor(HoleState.OccupiedByEvent);
                    _moleRenderer.gameObject.SetActive(false);
                    break;
            }

            if (_moleRenderer != null)
            {
                float yOffset = State switch
                {
                    HoleState.Warning => 0.02f,
                    HoleState.HitWindow => 0.16f,
                    HoleState.HitFlash => 0.18f,
                    HoleState.Retreat => 0.06f,
                    HoleState.Cooldown => -0.01f,
                    _ => 0.14f,
                };
                Vector3 current = _moleRenderer.transform.localPosition;
                _moleRenderer.transform.localPosition = new Vector3(0f, yOffset, current.z);
            }

            if (_moleRenderer.gameObject.activeSelf)
            {
                ApplyMoleReadabilityOverlay();
                if (_stateBlendTimer > 0f)
                {
                    float t = 1f - Mathf.Clamp01(_stateBlendTimer / StateBlendDuration);
                    Color c = _moleRenderer.color;
                    c.a *= Mathf.Lerp(0.66f, 1f, t);
                    _moleRenderer.color = c;
                    _moleRenderer.transform.localScale *= Mathf.Lerp(0.92f, 1f, t);
                }
            }

            if (Facility != null)
            {
                Color overlay = ResolveFacilityOverlayColor();
                _holeRenderer.color = Color.Lerp(_holeRenderer.color, overlay, _hasHoleArtSprite ? 0.18f : 0.32f);
            }

            if (_eventPressureActive)
            {
                _holeRenderer.color = Color.Lerp(_holeRenderer.color, new Color(0.98f, 0.3f, 0.22f), 0.45f);
            }
        }

        private void FitHoleScale(float targetHeight, float minTargetWidth)
        {
            if (_holeRenderer == null)
            {
                return;
            }

            Sprite sprite = _holeRenderer.sprite;
            if (sprite == null)
            {
                _holeRenderer.transform.localScale = new Vector3(1.05f, 0.44f, 1f);
                return;
            }

            float spriteHeight = Mathf.Max(0.001f, sprite.bounds.size.y);
            float spriteWidth = Mathf.Max(0.001f, sprite.bounds.size.x);
            float scaleByHeight = targetHeight / spriteHeight;
            float scaleByWidth = minTargetWidth / spriteWidth;
            float scale = Mathf.Max(scaleByHeight, scaleByWidth);
            _holeRenderer.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private void FitMoleScale(float targetHeight)
        {
            if (_moleRenderer == null)
            {
                return;
            }

            Sprite sprite = _moleRenderer.sprite;
            if (sprite == null)
            {
                _moleRenderer.transform.localScale = new Vector3(targetHeight, targetHeight, 1f);
                return;
            }

            float spriteHeight = Mathf.Max(0.001f, sprite.bounds.size.y);
            float scale = targetHeight / spriteHeight;
            _moleRenderer.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private void ApplyMoleReadabilityOverlay()
        {
            if (CurrentMole == null || _moleRenderer == null)
            {
                return;
            }

            if (CurrentMole.Def.Rarity >= Rarity.Epic)
            {
                Color rareAccent = CurrentMole.Def.Rarity == Rarity.Legendary
                    ? new Color(1f, 0.85f, 0.35f)
                    : new Color(0.95f, 0.6f, 1f);
                _moleRenderer.color = Color.Lerp(_moleRenderer.color, rareAccent, 0.3f);
                _moleRenderer.transform.localScale *= CurrentMole.Def.Rarity == Rarity.Legendary ? 1.12f : 1.07f;
            }

            if (_eventPressureActive)
            {
                _moleRenderer.color = Color.Lerp(_moleRenderer.color, new Color(1f, 0.58f, 0.32f), 0.22f);
            }
        }

        private Color ResolveHoleColor(HoleState state)
        {
            if (_hasHoleArtSprite)
            {
                return state switch
                {
                    HoleState.Idle => new Color(0.92f, 0.93f, 0.94f),
                    HoleState.Warning => new Color(1f, 0.9f, 0.72f),
                    HoleState.HitWindow => new Color(0.86f, 1f, 0.88f),
                    HoleState.HitFlash => new Color(1f, 0.82f, 0.82f),
                    HoleState.Retreat => new Color(0.88f, 0.88f, 0.93f),
                    HoleState.Cooldown => new Color(0.82f, 0.84f, 0.88f),
                    HoleState.OccupiedByEvent => new Color(0.84f, 0.78f, 0.96f),
                    _ => Color.white,
                };
            }

            if (_presentationSkin == null)
            {
                return state switch
                {
                    HoleState.Idle => new Color(0.22f, 0.16f, 0.1f),
                    HoleState.Warning => new Color(0.5f, 0.38f, 0.2f),
                    HoleState.HitWindow => new Color(0.12f, 0.4f, 0.14f),
                    HoleState.HitFlash => new Color(0.8f, 0.2f, 0.2f),
                    HoleState.Retreat => new Color(0.2f, 0.16f, 0.2f),
                    HoleState.Cooldown => new Color(0.15f, 0.12f, 0.1f),
                    HoleState.OccupiedByEvent => new Color(0.18f, 0.15f, 0.3f),
                    _ => new Color(0.22f, 0.16f, 0.1f),
                };
            }

            Color cooldown = _presentationSkin.HoleCooldownColor;
            return state switch
            {
                HoleState.Idle => _presentationSkin.HoleIdleColor,
                HoleState.Warning => _presentationSkin.HoleWarningColor,
                HoleState.HitWindow => _presentationSkin.HoleActiveColor,
                HoleState.HitFlash => _presentationSkin.HoleHitColor,
                HoleState.Retreat => Color.Lerp(cooldown, new Color(0.24f, 0.16f, 0.24f), 0.5f),
                HoleState.Cooldown => cooldown,
                HoleState.OccupiedByEvent => Color.Lerp(cooldown, new Color(0.32f, 0.2f, 0.44f), 0.5f),
                _ => _presentationSkin.HoleIdleColor,
            };
        }

        private MoleVisualEntry ResolveCurrentMoleVisualEntry()
        {
            if (_moleVisualLookup == null)
            {
                return null;
            }

            MoleDef visualDef = CurrentMole != null ? CurrentMole.Def : _lastMoleDef;
            if (visualDef == null)
            {
                return null;
            }

            if (_moleVisualLookup.TryGetValue(visualDef.Id, out MoleVisualEntry direct))
            {
                return direct;
            }

            string alias = ResolveMoleVisualAlias(visualDef);
            if (!string.IsNullOrWhiteSpace(alias) &&
                _moleVisualLookup.TryGetValue(alias, out MoleVisualEntry mapped))
            {
                return mapped;
            }

            return null;
        }

        private static string ResolveMoleVisualAlias(MoleDef def)
        {
            if (def == null)
            {
                return string.Empty;
            }

            string upperId = (def.Id ?? string.Empty).ToUpperInvariant();
            if (upperId.Contains("COMMON"))
            {
                return "mole_common";
            }

            if (upperId.Contains("SWIFT") || upperId.Contains("FAST"))
            {
                return "mole_swift";
            }

            if (upperId.Contains("TANK") || upperId.Contains("ARMORED"))
            {
                return "mole_tank";
            }

            if (upperId.Contains("BOMB") || upperId.Contains("EXPLO"))
            {
                return "mole_bomb";
            }

            if (upperId.Contains("CHEST") || upperId.Contains("TREASURE"))
            {
                return "mole_chest";
            }

            if (upperId.Contains("CHAIN") || upperId.Contains("ELECT"))
            {
                return "mole_chain";
            }

            if (upperId.Contains("SHIELD"))
            {
                return "mole_shield";
            }

            if (upperId.Contains("ELITE") || upperId.Contains("COMMAND") || upperId.Contains("LEGEND"))
            {
                return "mole_elite";
            }

            if (def.Traits.HasFlag(MoleTrait.Elite))
            {
                return "mole_elite";
            }

            if (def.Traits.HasFlag(MoleTrait.Shield))
            {
                return "mole_shield";
            }

            if (def.Traits.HasFlag(MoleTrait.Chain))
            {
                return "mole_chain";
            }

            if (def.Traits.HasFlag(MoleTrait.Chest))
            {
                return def.Rarity >= Rarity.Epic ? "mole_chest" : "mole_common";
            }

            if (def.Traits.HasFlag(MoleTrait.Bomb))
            {
                return "mole_bomb";
            }

            if (def.Traits.HasFlag(MoleTrait.Tank))
            {
                return "mole_tank";
            }

            if (def.Traits.HasFlag(MoleTrait.Fast))
            {
                return "mole_swift";
            }

            return "mole_common";
        }

        private Color ResolveCurrentMoleTint()
        {
            MoleDef visualDef = CurrentMole != null ? CurrentMole.Def : _lastMoleDef;
            if (visualDef == null)
            {
                return Color.white;
            }

            MoleVisualEntry entry = ResolveCurrentMoleVisualEntry();
            if (entry != null)
            {
                return entry.Tint;
            }

            return visualDef.TintColor;
        }

        private Sprite ResolveCurrentMoleSprite(HoleState state)
        {
            MoleVisualEntry entry = ResolveCurrentMoleVisualEntry();
            if (entry != null)
            {
                Sprite active = entry.ActiveSprite != null
                    ? entry.ActiveSprite
                    : (entry.IdleSprite != null ? entry.IdleSprite : entry.Sprite);
                Sprite warning = entry.WarningSprite != null
                    ? entry.WarningSprite
                    : (entry.RecoverSprite != null ? entry.RecoverSprite : active);
                Sprite retreat = entry.RetreatSprite != null
                    ? entry.RetreatSprite
                    : (entry.RecoverSprite != null ? entry.RecoverSprite : warning);

                if (state == HoleState.Warning && warning != null)
                {
                    return warning;
                }

                if (state == HoleState.HitFlash)
                {
                    if (entry.HitSprite != null && entry.HitSpriteAlt != null)
                    {
                        int phase = Mathf.FloorToInt(Time.time * 20f) % 2;
                        return phase == 0 ? entry.HitSprite : entry.HitSpriteAlt;
                    }

                    if (entry.HitSprite != null)
                    {
                        return entry.HitSprite;
                    }
                }

                if (state == HoleState.Retreat || state == HoleState.Cooldown)
                {
                    if (retreat != null)
                    {
                        return retreat;
                    }
                }

                if (active != null)
                {
                    return active;
                }
            }

            if (_presentationSkin != null && _presentationSkin.MoleDefaultSprite != null)
            {
                return _presentationSkin.MoleDefaultSprite;
            }

            return SpriteCache.PlaceholderBlockSprite;
        }

        private Color ResolveFacilityOverlayColor()
        {
            if (Facility == null)
            {
                return Color.white;
            }

            return Facility.State switch
            {
                FacilityState.Trigger => new Color(0.95f, 0.66f, 0.18f),
                FacilityState.Overload => new Color(0.95f, 0.26f, 0.26f),
                FacilityState.Cooldown => new Color(0.36f, 0.58f, 0.86f),
                _ => new Color(0.65f, 0.75f, 0.95f),
            };
        }

        private void RefreshFacilityLabel()
        {
            if (_facilityText == null)
            {
                return;
            }

            if (Facility == null)
            {
                _facilityText.gameObject.SetActive(false);
                _facilityText.text = string.Empty;
                return;
            }

            _facilityText.gameObject.SetActive(true);
            string shortName = Facility.Type switch
            {
                FacilityType.AutoHammerTower => "锤塔",
                FacilityType.SensorHammer => "雷锤",
                FacilityType.GoldMagnet => "吸金",
                FacilityType.BountyMarker => "赏金",
                FacilityType.TeslaCoupler => "电网",
                FacilityType.ExecutionPlate => "处决",
                _ => "设施",
            };
            string stateText = Facility.State switch
            {
                FacilityState.Trigger => "!",
                FacilityState.Overload => "超",
                FacilityState.Cooldown => "冷",
                _ => string.Empty,
            };
            _facilityText.text = $"{shortName}{stateText}";
            _facilityText.color = ResolveFacilityOverlayColor();
        }
    }

    public sealed class DropRuntime
    {
        private readonly Transform _transform;

        public DropRuntime(
            DropType type,
            int amount,
            Vector2 position,
            Transform parent,
            Sprite visualSprite = null,
            Color? tintOverride = null)
        {
            Type = type;
            Amount = amount;
            Position = position;
            Age = 0f;
            Vector2 randomImpulse = UnityEngine.Random.insideUnitCircle * 0.8f;
            Velocity = new Vector2(randomImpulse.x, Mathf.Abs(randomImpulse.y) + 0.8f);

            GameObject visual = new GameObject($"Drop_{type}");
            visual.transform.SetParent(parent, false);
            visual.transform.position = position;
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = visualSprite != null ? visualSprite : SpriteCache.WhiteSprite;
            renderer.sortingOrder = 20;
            renderer.color = tintOverride ?? ResolveDefaultTint(type);
            visual.transform.localScale = Vector3.one * 0.16f;
            VisualObject = visual;
            _transform = visual.transform;
        }

        public DropType Type { get; }

        public int Amount { get; }

        public Vector2 Position { get; private set; }

        public Vector2 Velocity { get; private set; }

        public float Age { get; private set; }

        public bool Collected { get; private set; }

        public GameObject VisualObject { get; }

        public bool ShouldExpire => Age > 8f;

        public void Tick(float deltaTime, Vector2 magnetTarget, float magnetRadius)
        {
            if (Collected)
            {
                return;
            }

            Age += deltaTime;

            Vector2 targetDir = magnetTarget - Position;
            float dist = targetDir.magnitude;
            if (magnetRadius > 0f && dist <= magnetRadius)
            {
                Vector2 dir = dist > 0.001f ? targetDir / dist : Vector2.zero;
                Velocity = Vector2.Lerp(Velocity, dir * 6.5f, deltaTime * 7f);
            }
            else
            {
                Velocity += Vector2.down * 4.8f * deltaTime;
                if (Position.y < -5.5f)
                {
                    Vector2 adjusted = Velocity;
                    adjusted.y = Mathf.Abs(adjusted.y) * 0.5f;
                    Velocity = adjusted;
                }
            }

            Position += Velocity * deltaTime;
            if (_transform != null)
            {
                _transform.position = Position;
            }
        }

        public void MarkCollected()
        {
            Collected = true;
            if (VisualObject != null)
            {
                UnityEngine.Object.Destroy(VisualObject);
            }
        }

        private static Color ResolveDefaultTint(DropType type)
        {
            return type switch
            {
                DropType.Gold => new Color(0.95f, 0.82f, 0.26f),
                DropType.Experience => new Color(0.45f, 0.95f, 0.95f),
                DropType.Core => new Color(0.95f, 0.35f, 0.95f),
                _ => Color.white,
            };
        }
    }

    public sealed class BossRuntime
    {
        private readonly SpriteRenderer _renderer;
        private readonly SpriteRenderer _outlineRenderer;
        private readonly Color _baseTint;

        public BossRuntime(BossDef def, Transform parent, Sprite spriteOverride = null, Color? tintOverride = null)
        {
            Def = def;
            Root = new GameObject("Boss");
            Root.transform.SetParent(parent, false);
            Root.transform.position = new Vector3(0f, 0.2f, 0f);
            Sprite bossSprite = spriteOverride != null ? spriteOverride : SpriteCache.WhiteSprite;

            GameObject outline = new GameObject("Outline");
            outline.transform.SetParent(Root.transform, false);
            outline.transform.localScale = new Vector3(1.11f, 1.11f, 1f);
            _outlineRenderer = outline.AddComponent<SpriteRenderer>();
            _outlineRenderer.sprite = bossSprite;
            _outlineRenderer.sortingOrder = 24;
            _outlineRenderer.color = new Color(0f, 0f, 0f, 0.55f);

            _renderer = Root.AddComponent<SpriteRenderer>();
            _renderer.sprite = bossSprite;
            _renderer.sortingOrder = 25;
            _baseTint = tintOverride ?? def.TintColor;
            _renderer.color = _baseTint;
            Root.transform.localScale = new Vector3(1.9f, 1.9f, 1f);
            Root.SetActive(false);
            HpText = CreateWorldText(parent, new Vector3(0f, 1.5f, 0f), 0.32f, TextAnchor.MiddleCenter, Color.white);
            HpText.gameObject.SetActive(false);
        }

        public BossDef Def { get; }

        public GameObject Root { get; }

        public TextMesh HpText { get; }

        public bool Active { get; private set; }

        public float RemainingHp { get; private set; }

        public float MaxHp { get; private set; }

        public float AttackTimer { get; private set; }

        public bool ShieldActive { get; private set; }

        public float ShieldDamageMultiplier { get; set; } = 0.35f;

        public void Activate(float hpMultiplier)
        {
            Active = true;
            MaxHp = Mathf.Max(1f, Def.Hp * hpMultiplier);
            RemainingHp = MaxHp;
            AttackTimer = Def.AttackInterval;
            SetShieldActive(false);
            _renderer.color = _baseTint;
            Root.SetActive(true);
            // World-space HP text is kept off now that HUD boss bars are available.
            HpText.gameObject.SetActive(false);
            RefreshHpText();
        }

        public void Deactivate()
        {
            Active = false;
            MaxHp = 0f;
            SetShieldActive(false);
            Root.SetActive(false);
            HpText.gameObject.SetActive(false);
        }

        public bool Tick(float deltaTime)
        {
            if (!Active)
            {
                return false;
            }

            AttackTimer -= deltaTime;
            if (AttackTimer <= 0f)
            {
                AttackTimer = Def.AttackInterval;
                return true;
            }

            if (ShieldActive)
            {
                float pulse = 0.72f + Mathf.Sin(Time.time * 13f) * 0.12f;
                _renderer.color = Color.Lerp(_baseTint, new Color(0.38f, 0.74f, 1f), pulse);
                _outlineRenderer.color = Color.Lerp(new Color(0.08f, 0.16f, 0.45f, 0.6f), new Color(0.2f, 0.72f, 1f, 0.82f), pulse);
            }
            else if (_outlineRenderer != null)
            {
                float pulse = 0.5f + Mathf.Sin(Time.time * 7f) * 0.5f;
                _outlineRenderer.color = Color.Lerp(new Color(0f, 0f, 0f, 0.48f), new Color(1f, 0.62f, 0.2f, 0.28f), pulse * 0.4f);
            }

            return false;
        }

        public bool ApplyDamage(float amount)
        {
            if (!Active)
            {
                return false;
            }

            float applied = Mathf.Max(0f, amount);
            if (ShieldActive)
            {
                applied *= Mathf.Clamp01(ShieldDamageMultiplier);
            }

            RemainingHp -= applied;
            RefreshHpText();
            if (RemainingHp <= 0f)
            {
                Active = false;
                MaxHp = 0f;
                SetShieldActive(false);
                Root.SetActive(false);
                HpText.gameObject.SetActive(false);
                return true;
            }

            return false;
        }

        public void SetShieldActive(bool active)
        {
            ShieldActive = active;
            if (_renderer != null)
            {
                _renderer.color = active ? Color.Lerp(_baseTint, new Color(0.38f, 0.74f, 1f), 0.88f) : _baseTint;
            }

            if (_outlineRenderer != null)
            {
                _outlineRenderer.color = active
                    ? new Color(0.2f, 0.72f, 1f, 0.82f)
                    : new Color(0f, 0f, 0f, 0.55f);
            }

            RefreshHpText();
        }

        private void RefreshHpText()
        {
            if (HpText != null)
            {
                string shieldText = ShieldActive ? " [护盾]" : string.Empty;
                HpText.text = $"Boss HP {Mathf.CeilToInt(Mathf.Max(0f, RemainingHp))}{shieldText}";
            }
        }

        private static TextMesh CreateWorldText(Transform parent, Vector3 localPos, float size, TextAnchor anchor, Color color)
        {
            GameObject go = new GameObject("BossHpText");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            TextMesh textMesh = go.AddComponent<TextMesh>();
            textMesh.anchor = anchor;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = size;
            textMesh.fontSize = 48;
            textMesh.color = color;
            return textMesh;
        }
    }

    public static class SpriteCache
    {
        private static Sprite _whiteSprite;
        private static Sprite _placeholderBlockSprite;
        private static Sprite _holeFallbackSprite;
        private static Sprite _holeLipSprite;
        private static Sprite _holeCoreSprite;

        public static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite != null)
                {
                    return _whiteSprite;
                }

                Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    name = "MS_WhiteTex",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
                _whiteSprite.name = "MS_WhiteSprite";
                return _whiteSprite;
            }
        }

        public static Sprite PlaceholderBlockSprite
        {
            get
            {
                if (_placeholderBlockSprite != null)
                {
                    return _placeholderBlockSprite;
                }

                const int size = 32;
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "MS_PlaceholderBlockTex",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };

                Color inner = new Color(1f, 1f, 1f, 1f);
                Color border = new Color(0.08f, 0.08f, 0.08f, 1f);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        bool isBorder = x <= 1 || y <= 1 || x >= size - 2 || y >= size - 2;
                        tex.SetPixel(x, y, isBorder ? border : inner);
                    }
                }

                tex.Apply();
                _placeholderBlockSprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, size, size),
                    new Vector2(0.5f, 0.5f),
                    100f);
                _placeholderBlockSprite.name = "MS_PlaceholderBlockSprite";
                return _placeholderBlockSprite;
            }
        }

        public static Sprite HoleFallbackSprite
        {
            get
            {
                if (_holeFallbackSprite != null)
                {
                    return _holeFallbackSprite;
                }

                const int width = 160;
                const int height = 84;
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "MS_HoleFallbackTex",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };

                float cx = (width - 1) * 0.5f;
                float cy = (height - 1) * 0.5f;
                float rx = width * 0.46f;
                float ry = height * 0.4f;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float dx = (x - cx) / rx;
                        float dy = (y - cy) / ry;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        if (r > 1f)
                        {
                            tex.SetPixel(x, y, Color.clear);
                            continue;
                        }

                        float ring = Mathf.InverseLerp(1f, 0.65f, r);
                        float inner = Mathf.InverseLerp(0.58f, 0f, r);
                        Color col = Color.Lerp(new Color(0.08f, 0.1f, 0.12f, 0.85f), new Color(0.66f, 0.74f, 0.82f, 0.95f), ring);
                        col = Color.Lerp(col, new Color(0.03f, 0.04f, 0.05f, 0.98f), inner * 0.82f);
                        tex.SetPixel(x, y, col);
                    }
                }

                tex.Apply();
                _holeFallbackSprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, width, height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                _holeFallbackSprite.name = "MS_HoleFallbackSprite";
                return _holeFallbackSprite;
            }
        }

        public static Sprite HoleCoreSprite
        {
            get
            {
                if (_holeCoreSprite != null)
                {
                    return _holeCoreSprite;
                }

                const int width = 128;
                const int height = 68;
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "MS_HoleCoreTex",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };

                float cx = (width - 1) * 0.5f;
                float cy = (height - 1) * 0.5f;
                float rx = width * 0.47f;
                float ry = height * 0.42f;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float dx = (x - cx) / rx;
                        float dy = (y - cy) / ry;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        if (r > 1f)
                        {
                            tex.SetPixel(x, y, Color.clear);
                            continue;
                        }

                        float alpha = Mathf.Lerp(0.96f, 0.2f, Mathf.Clamp01(r));
                        tex.SetPixel(x, y, new Color(0.04f, 0.05f, 0.06f, alpha));
                    }
                }

                tex.Apply();
                _holeCoreSprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, width, height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                _holeCoreSprite.name = "MS_HoleCoreSprite";
                return _holeCoreSprite;
            }
        }

        public static Sprite HoleLipSprite
        {
            get
            {
                if (_holeLipSprite != null)
                {
                    return _holeLipSprite;
                }

                const int width = 180;
                const int height = 92;
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "MS_HoleLipTex",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };

                float cx = (width - 1) * 0.5f;
                float cy = height * 0.58f;
                float rx = width * 0.46f;
                float ry = height * 0.38f;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float dx = (x - cx) / rx;
                        float dy = (y - cy) / ry;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        if (r > 1.02f || y > cy + 2f)
                        {
                            tex.SetPixel(x, y, Color.clear);
                            continue;
                        }

                        if (y < cy - 12f)
                        {
                            tex.SetPixel(x, y, Color.clear);
                            continue;
                        }

                        float band = Mathf.InverseLerp(1.02f, 0.72f, r);
                        float alpha = Mathf.Clamp01(band) * Mathf.InverseLerp(cy - 12f, cy + 2f, y) * 0.92f;
                        Color edge = Color.Lerp(new Color(0.86f, 0.9f, 0.95f, 0f), new Color(0.72f, 0.78f, 0.84f, alpha), band);
                        tex.SetPixel(x, y, edge);
                    }
                }

                tex.Apply();
                _holeLipSprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, width, height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                _holeLipSprite.name = "MS_HoleLipSprite";
                return _holeLipSprite;
            }
        }
    }

    public sealed partial class DemoGameController : MonoBehaviour
    {
        private const float DefaultRunDurationSeconds = 600f;
        private const float DefaultBossGraceSeconds = 60f;
        private const string DefaultSkinResourcePath = "MoleSurvivors/DefaultPresentationSkin";
        private const string OpeningRouteAutoTower = "auto_tower";
        private const string OpeningRouteChainGrid = "chain_grid";
        private const string OpeningRouteBountyFactory = "bounty_factory";
        private static readonly FacilityType[] StarterFacilityTypes =
        {
            FacilityType.AutoHammerTower,
            FacilityType.SensorHammer,
            FacilityType.GoldMagnet,
        };
        private static readonly float[] StarterFacilityWeights = { 0.45f, 0.35f, 0.2f };
        private static Font _uiFont;

        [Header("Presentation")]
        [SerializeField]
        private PresentationSkin _presentationSkin;

        [SerializeField]
        private string _skinResourcePath = DefaultSkinResourcePath;

        [Header("External Art Pack")]
        [SerializeField]
        private bool _enableExternalArtPack = true;

        [SerializeField]
        private string _externalArtRelativePath = "Art/Temp/Round4_Nano";

        [SerializeField]
        private bool _enableExternalUiPack = true;

        [SerializeField]
        private string _externalUiRelativePath = "Art/Temp/FreeUI";

        [Header("Handfeel")]
        [SerializeField]
        private float _hitStopSeconds = 0.03f;

        [SerializeField]
        private float _critHitStopSeconds = 0.05f;

        [SerializeField]
        private float _bossHitStopSeconds = 0.045f;

        [SerializeField]
        private float _cameraShakeSeconds = 0.12f;

        [SerializeField]
        private float _cameraShakeAmplitude = 0.07f;

        [SerializeField]
        private float _cameraShakeFrequency = 40f;

        private readonly List<HoleRuntime> _holes = new List<HoleRuntime>();
        private readonly List<DropRuntime> _drops = new List<DropRuntime>();
        private readonly Dictionary<string, MoleVisualEntry> _moleVisualLookup =
            new Dictionary<string, MoleVisualEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<DropType, DropVisualEntry> _dropVisualLookup = new Dictionary<DropType, DropVisualEntry>();

        private System.Random _random;
        private GameContent _content;
        private ISaveRepository _saveRepository;
        private IUpgradeOfferService _upgradeOfferService;
        private IUpgradeVisualizationService _upgradeVisualizationService;
        private ISpawnDirector _spawnDirector;
        private IAutomationService _automationService;
        private IFacilityService _facilityService;
        private IBossEncounterService _bossEncounterService;
        private IFtueService _ftueService;
        private IEventChoiceService _eventChoiceService;
        private AchievementService _achievementService;
        private DifficultyModeDef _activeDifficulty;
        private ChallengeModDef _activeChallenge;
        private WaveModDef _activeWaveMod;
        private readonly Dictionary<string, string> _localizationLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private MetaProgressState _meta;
        private RunState _run;
        private SpawnerState _spawner;
        private AutomationState _automation;

        private Camera _camera;
        private Vector3 _cameraBasePosition;
        private Transform _worldRoot;
        private Transform _dropRoot;
        private BossRuntime _boss;
        private readonly Dictionary<string, BossRuntime> _bossLookup = new Dictionary<string, BossRuntime>();
        private BossEncounterRuntime _activeBossEncounter;
        private List<BossEncounterRuntime> _bossTimeline = new List<BossEncounterRuntime>();
        private float _bossSpawnScale = 1f;
        private readonly HashSet<int> _rogueHoleIndices = new HashSet<int>();
        private AudioSource _sfxSource;
        private bool _holeSpriteHealthChecked;
        private bool _holeSpriteUsable = true;

        private Canvas _canvas;
        private Text _topHud;
        private Text _rightHud;
        private Text _bottomHud;
        private Text _centerMessage;
        private RectTransform _bossBarRoot;
        private Image _bossBarBackground;
        private Image _bossBarFill;
        private Image _bossBarShieldFill;
        private Image _bossBarFrame;
        private Image _bossBarWarnGlow;
        private Text _bossBarLabel;
        private RectTransform _durabilityBarRoot;
        private Image _durabilityBarFill;
        private Image _durabilityBarFrame;
        private Image _durabilityBarDangerOverlay;
        private Text _durabilityBarLabel;
        private RectTransform _expBarRoot;
        private Image _expBarFill;
        private Image _expBarFrame;
        private Image _expBarLevelFlash;
        private Text _expBarLabel;
        private RectTransform _comboBarRoot;
        private Image _comboBarFill;
        private Image _comboBarFrame;
        private Image _comboBarMaxState;
        private Text _comboBarLabel;
        private Image _alertFlashOverlay;
        private float _alertFlashTimer;
        private float _alertFlashDuration;
        private Color _alertFlashColor;
        private float _messageTimer;
        private float _expBarFlashTimer;

        private GameObject _upgradePanel;
        private Text _upgradeTitle;
        private readonly Button[] _upgradeButtons = new Button[3];
        private readonly Text[] _upgradeButtonTexts = new Text[3];
        private readonly Image[] _upgradeCardBackgrounds = new Image[3];
        private readonly Image[] _upgradeCardFrames = new Image[3];
        private readonly Image[] _upgradeCardIcons = new Image[3];
        private List<UpgradeDef> _currentOffer = new List<UpgradeDef>();

        private GameObject _eventPanel;
        private Text _eventText;
        private Image _eventIcon;
        private Button _eventAcceptButton;
        private Button _eventSkipButton;
        private Button _eventRerollButton;
        private RunEventDef _pendingEvent;
        private bool _merchantShopMode;
        private readonly List<UpgradeDef> _merchantShopOffers = new List<UpgradeDef>(3);
        private readonly List<int> _merchantShopCosts = new List<int>(3);

        private GameObject _endPanel;
        private Image _endPanelImage;
        private Text _endSummary;
        private Image _endResultStamp;

        private GameObject _metaPanel;
        private Text _metaHeader;
        private Transform _metaListRoot;
        private Button _closeMetaButton;

        private float _manualAttackCooldown;
        private float _botAttackTimer;
        private float _hitStopTimer;
        private float _shakeTimer;
        private float _shakeStrength;
        private float _shakeSeedX;
        private float _shakeSeedY;
        private float _rareHintCooldown;
        private float _earlyReliefRepairTimer;
        private float _starterAutomationTargetSecond = -1f;
        private int _messagePriority;
        private string _activeArtSummary = "占位";
        private string _activeUiSummary = "默认";
        private ExternalUiSkin _externalUiSkin;
        private bool _isInitialized;
        private bool _upgradeOpen;
        private bool _eventOpen;
        private bool _metaOpen;
        private bool _endOpen;
        private bool _bossWarningShown;
        private bool _midBossWarningShown;
        private const int MaxRecentRunHistory = 8;

        public bool EnableAutoPilotForTests { get; set; }

        public RunState CurrentRun => _run;

        public MetaProgressState MetaState => _meta;

        public bool BossSpawned => _run != null && _run.BossSpawned;

        public bool MidBossSpawned => _run != null && _run.MidBossSpawned;

        public PresentationSkin ActivePresentationSkin => _presentationSkin;

        public int ActiveFacilityCount => _holes.Count(h => h.Facility != null);

        public float EarlyCommonAverageManualHits =>
            _run != null && _run.EarlyCommonKillSamples > 0 ? _run.EarlyCommonManualHitAverage : -1f;

        public int EarlyCommonSampleCount => _run != null ? _run.EarlyCommonKillSamples : 0;

        public string LastEditorHotReloadMessage { get; private set; } = "Not started.";

        private void Awake()
        {
            DemoGameController existing = FindObjectsOfType<DemoGameController>()
                .FirstOrDefault(controller => controller != this);
            if (existing != null)
            {
                Destroy(gameObject);
                return;
            }

            _random = new System.Random();
            _content = DefaultContentFactory.CreateDefault();
            RebuildLocalizationLookup();
            ApplyConfigDrivenPresentationSettings();
            InitializeFlowSystems();
            _saveRepository = new JsonSaveRepository(
                defaultUnlockedWeapons: _content.StartupUnlockedWeaponIds,
                defaultUnlockedCharacters: _content.StartupUnlockedCharacterIds,
                defaultWeaponId: ResolveConfiguredDefaultWeaponId(),
                defaultCharacterId: ResolveConfiguredDefaultCharacterId());
            _upgradeOfferService = new UpgradeOfferService();
            _upgradeVisualizationService = new UpgradeVisualizationService();
            _spawnDirector = new SpawnDirector();
            _automationService = new AutomationService();
            _facilityService = new FacilityService();
            _bossEncounterService = new BossEncounterService();
            _ftueService = new FtueService();
            _eventChoiceService = new EventChoiceService();
            _achievementService = new AchievementService();

            _meta = _saveRepository.LoadOrCreate();
            NormalizeMetaStateAgainstContent();
            ResolvePresentationSkin();
            ResolveExternalUiSkin();

            EnsureCamera();
            EnsureAudioSource();
            ApplyClientSettings(false);
            EnsureEventSystem();
            BuildWorld();
            BuildUI();
            if (ShouldAutoStartRunOnBoot())
            {
                StartRun();
            }
            else
            {
                OpenMainMenu();
            }

            _isInitialized = true;
        }

        private void ResolvePresentationSkin()
        {
            if (_presentationSkin == null)
            {
                string resourcePath = string.IsNullOrWhiteSpace(_skinResourcePath)
                    ? DefaultSkinResourcePath
                    : _skinResourcePath.Trim();
                _presentationSkin = Resources.Load<PresentationSkin>(resourcePath);
                if (_presentationSkin == null && resourcePath != "PresentationSkin")
                {
                    _presentationSkin = Resources.Load<PresentationSkin>("PresentationSkin");
                }
            }

            if (_presentationSkin == null)
            {
                _presentationSkin = ScriptableObject.CreateInstance<PresentationSkin>();
            }

            if (_enableExternalArtPack)
            {
                ExternalArtPackReport report = ExternalArtPackLoader.TryApply(_presentationSkin, _externalArtRelativePath);
                if (report.Applied)
                {
                    _activeArtSummary = $"外部包 {report.LoadedSprites}张";
                    Debug.Log($"[MoleSurvivors] External art pack loaded: {report.PackDirectory} ({report.LoadedSprites} sprites)");
                }
                else
                {
                    _activeArtSummary = "占位";
                }
            }
            else
            {
                _activeArtSummary = "占位";
            }

            _moleVisualLookup.Clear();
            _dropVisualLookup.Clear();
            if (_presentationSkin == null)
            {
                return;
            }

            if (_presentationSkin.MoleVisuals != null)
            {
                for (int i = 0; i < _presentationSkin.MoleVisuals.Count; i++)
                {
                    MoleVisualEntry entry = _presentationSkin.MoleVisuals[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.MoleId))
                    {
                        continue;
                    }

                    if (!_moleVisualLookup.ContainsKey(entry.MoleId))
                    {
                        _moleVisualLookup.Add(entry.MoleId, entry);
                    }
                }
            }

            if (_presentationSkin.DropVisuals != null)
            {
                for (int i = 0; i < _presentationSkin.DropVisuals.Count; i++)
                {
                    DropVisualEntry entry = _presentationSkin.DropVisuals[i];
                    if (entry == null || _dropVisualLookup.ContainsKey(entry.DropType))
                    {
                        continue;
                    }

                    _dropVisualLookup.Add(entry.DropType, entry);
                }
            }
        }

        private void ResolveExternalUiSkin()
        {
            if (!_enableExternalUiPack)
            {
                _externalUiSkin = null;
                _activeUiSummary = "默认";
                return;
            }

            _externalUiSkin = ExternalUiPackLoader.TryLoad(_externalUiRelativePath);
            if (_externalUiSkin != null && _externalUiSkin.Loaded)
            {
                _activeUiSummary = $"外部包 {_externalUiSkin.LoadedSpriteCount}张";
                Debug.Log($"[MoleSurvivors] External UI pack loaded: {_externalUiSkin.PackDirectory} ({_externalUiSkin.LoadedSpriteCount} sprites)");
            }
            else
            {
                _activeUiSummary = "默认";
            }
        }

        private void RebuildLocalizationLookup()
        {
            _localizationLookup.Clear();
            if (_content == null || _content.LocalizationEntries == null)
            {
                return;
            }

            for (int i = 0; i < _content.LocalizationEntries.Count; i++)
            {
                LocalizationEntryDef entry = _content.LocalizationEntries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                string value = !string.IsNullOrWhiteSpace(entry.ZhCn) ? entry.ZhCn : entry.EnUs;
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                string trimmed = value.Trim();
                if (trimmed.StartsWith("示例文本", StringComparison.Ordinal))
                {
                    continue;
                }

                _localizationLookup[entry.Key.Trim()] = trimmed;
            }
        }

        private string L(string key, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(key) &&
                _localizationLookup.TryGetValue(key.Trim(), out string localized) &&
                !string.IsNullOrWhiteSpace(localized))
            {
                return localized;
            }

            return fallback;
        }

        public bool EditorHotReloadFromConfig()
        {
            if (!Application.isPlaying)
            {
                LastEditorHotReloadMessage = "Hot reload only works in Play Mode.";
                return false;
            }

            try
            {
                if (!ConfigDrivenContentLoader.TryLoad(out GameContent reloaded, out string sourceSummary))
                {
                    LastEditorHotReloadMessage = $"Config reload failed: {sourceSummary}";
                    Debug.LogWarning($"[MoleSurvivors] {LastEditorHotReloadMessage}");
                    return false;
                }

                _content = reloaded;
                RebuildLocalizationLookup();
                ApplyConfigDrivenPresentationSettings();
                RebuildCombatCueService();
                _saveRepository = new JsonSaveRepository(
                    defaultUnlockedWeapons: _content.StartupUnlockedWeaponIds,
                    defaultUnlockedCharacters: _content.StartupUnlockedCharacterIds,
                    defaultWeaponId: ResolveConfiguredDefaultWeaponId(),
                    defaultCharacterId: ResolveConfiguredDefaultCharacterId());
                if (_meta == null)
                {
                    _meta = _saveRepository.LoadOrCreate();
                }

                NormalizeMetaStateAgainstContent();
                _saveRepository.Save(_meta);
                ResolvePresentationSkin();
                ResolveExternalUiSkin();
                RebuildRuntimeForCurrentContent();
                LastEditorHotReloadMessage = $"Hot reload success: {sourceSummary}";
                Debug.Log($"[MoleSurvivors] {LastEditorHotReloadMessage}");
                return true;
            }
            catch (Exception ex)
            {
                LastEditorHotReloadMessage = $"Hot reload exception: {ex.Message}";
                Debug.LogError($"[MoleSurvivors] {LastEditorHotReloadMessage}\n{ex}");
                return false;
            }
        }

        private void RebuildRuntimeForCurrentContent()
        {
            _isInitialized = false;
            ClearDrops();
            _holes.Clear();
            _bossLookup.Clear();

            if (_worldRoot != null)
            {
                Destroy(_worldRoot.gameObject);
                _worldRoot = null;
                _dropRoot = null;
            }

            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
                _topHud = null;
                _rightHud = null;
                _bottomHud = null;
                _centerMessage = null;
                _bossBarRoot = null;
                _bossBarBackground = null;
                _bossBarFill = null;
                _bossBarShieldFill = null;
                _bossBarFrame = null;
                _bossBarWarnGlow = null;
                _bossBarLabel = null;
                _durabilityBarRoot = null;
                _durabilityBarFill = null;
                _durabilityBarFrame = null;
                _durabilityBarDangerOverlay = null;
                _durabilityBarLabel = null;
                _expBarRoot = null;
                _expBarFill = null;
                _expBarFrame = null;
                _expBarLevelFlash = null;
                _expBarLabel = null;
                _comboBarRoot = null;
                _comboBarFill = null;
                _comboBarFrame = null;
                _comboBarMaxState = null;
                _comboBarLabel = null;
                _alertFlashOverlay = null;
                _upgradePanel = null;
                _eventPanel = null;
                _eventAcceptButton = null;
                _eventSkipButton = null;
                _eventRerollButton = null;
                _endPanel = null;
                _endPanelImage = null;
                _endResultStamp = null;
                _metaPanel = null;
            }

            for (int i = 0; i < _upgradeButtons.Length; i++)
            {
                _upgradeCardBackgrounds[i] = null;
                _upgradeCardFrames[i] = null;
                _upgradeCardIcons[i] = null;
            }

            EnsureCamera();
            EnsureAudioSource();
            ApplyClientSettings(false);
            EnsureEventSystem();
            BuildWorld();
            BuildUI();
            StartRun();
            _isInitialized = true;
        }

        private void Update()
        {
            if (!_isInitialized)
            {
                return;
            }

            float unscaledDelta = Time.unscaledDeltaTime;
            TickImpactFeedback(unscaledDelta);
            TickAlertFlash(unscaledDelta);
            float gameplayDelta = _hitStopTimer > 0f ? 0f : Time.deltaTime;

            if (!_endOpen && !_metaOpen)
            {
                HandleInput(gameplayDelta);
            }

            if (!IsFlowOverlayBlockingGameplay() && !_upgradeOpen && !_eventOpen && !_metaOpen && !_endOpen)
            {
                TickRun(gameplayDelta);
            }

            if (_centerMessage != null)
            {
                _messageTimer -= gameplayDelta;
                if (_messageTimer <= 0f)
                {
                    _centerMessage.text = string.Empty;
                    _messagePriority = 0;
                }
            }

            TickRound7VisualFeedback(unscaledDelta, gameplayDelta);
            UpdateHud();
        }

        public void FastForwardForTests(float seconds, float step = 0.05f)
        {
            EnableAutoPilotForTests = true;
            float elapsed = 0f;
            while (elapsed < seconds && !_run.RunEnded)
            {
                if (_upgradeOpen && _currentOffer.Count > 0)
                {
                    OnUpgradeSelected(0);
                    continue;
                }

                if (_eventOpen)
                {
                    ResolveEventChoice(0);
                    continue;
                }

                if (_metaOpen || _endOpen)
                {
                    break;
                }

                TickRun(step);
                elapsed += step;
            }

            EnableAutoPilotForTests = false;
        }

        public void SetRandomSeedForTests(int seed)
        {
            _random = new System.Random(seed);
        }

        private void EnsureCamera()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                GameObject camObject = new GameObject("Main Camera");
                camObject.tag = "MainCamera";
                _camera = camObject.AddComponent<Camera>();
                camObject.AddComponent<AudioListener>();
            }

            _camera.orthographic = true;
            _camera.orthographicSize = 6f;
            _cameraBasePosition = new Vector3(0f, 0f, -10f);
            _camera.transform.position = _cameraBasePosition;
            _camera.backgroundColor = ResolveBackgroundColor();
            _camera.clearFlags = CameraClearFlags.SolidColor;

            _shakeSeedX = UnityEngine.Random.Range(0f, 500f);
            _shakeSeedY = UnityEngine.Random.Range(0f, 500f);
        }

        private void EnsureAudioSource()
        {
            _sfxSource = GetComponent<AudioSource>();
            if (_sfxSource == null)
            {
                _sfxSource = gameObject.AddComponent<AudioSource>();
            }

            _sfxSource.playOnAwake = false;
            _sfxSource.loop = false;
            _sfxSource.spatialBlend = 0f;
            _sfxSource.volume = 0.9f;
        }

        private Color ResolveBackgroundColor()
        {
            return _presentationSkin != null ? _presentationSkin.BackgroundColor : new Color(0.1f, 0.19f, 0.14f);
        }

        private Sprite ResolveBackgroundSprite()
        {
            if (_presentationSkin != null && _presentationSkin.BackgroundSprite != null)
            {
                return _presentationSkin.BackgroundSprite;
            }

            return SpriteCache.WhiteSprite;
        }

        private Sprite ResolveHoleSprite()
        {
            if (_presentationSkin != null && _presentationSkin.HoleSprite != null)
            {
                if (!_holeSpriteHealthChecked)
                {
                    _holeSpriteUsable = IsHoleSpriteUsable(_presentationSkin.HoleSprite);
                    _holeSpriteHealthChecked = true;
                }

                if (_holeSpriteUsable)
                {
                    return _presentationSkin.HoleSprite;
                }
            }

            return SpriteCache.HoleFallbackSprite;
        }

        private Sprite ResolveHoleForegroundSprite()
        {
            return SpriteCache.HoleLipSprite;
        }

        private static bool IsHoleSpriteUsable(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null)
            {
                return false;
            }

            Texture2D texture = sprite.texture;
            Rect rect = sprite.rect;
            if (rect.width < 4f || rect.height < 4f)
            {
                return false;
            }

            try
            {
                int sampleCols = 10;
                int sampleRows = 6;
                int opaque = 0;
                int sampleCount = sampleCols * sampleRows;
                for (int y = 0; y < sampleRows; y++)
                {
                    float ty = (y + 0.5f) / sampleRows;
                    int py = Mathf.Clamp(Mathf.RoundToInt(rect.y + rect.height * ty), 0, texture.height - 1);
                    for (int x = 0; x < sampleCols; x++)
                    {
                        float tx = (x + 0.5f) / sampleCols;
                        int px = Mathf.Clamp(Mathf.RoundToInt(rect.x + rect.width * tx), 0, texture.width - 1);
                        if (texture.GetPixel(px, py).a > 0.16f)
                        {
                            opaque++;
                        }
                    }
                }

                float coverage = opaque / (float)sampleCount;
                int cx = Mathf.Clamp(Mathf.RoundToInt(rect.center.x), 0, texture.width - 1);
                int cy = Mathf.Clamp(Mathf.RoundToInt(rect.center.y), 0, texture.height - 1);
                float centerAlpha = texture.GetPixel(cx, cy).a;
                return coverage >= 0.22f && centerAlpha >= 0.14f;
            }
            catch
            {
                return true;
            }
        }

        private Sprite ResolveDefaultMoleSprite()
        {
            if (_presentationSkin != null && _presentationSkin.MoleDefaultSprite != null)
            {
                return _presentationSkin.MoleDefaultSprite;
            }

            return SpriteCache.PlaceholderBlockSprite;
        }

        private Sprite ResolveBossSprite(BossDef bossDef)
        {
            if (bossDef != null &&
                IsMidBossId(bossDef.Id) &&
                _presentationSkin != null &&
                _presentationSkin.MidBossSprite != null)
            {
                return _presentationSkin.MidBossSprite;
            }

            if (_presentationSkin != null && _presentationSkin.BossSprite != null)
            {
                return _presentationSkin.BossSprite;
            }

            return SpriteCache.PlaceholderBlockSprite;
        }

        private Color ResolveBossTint(BossDef bossDef)
        {
            if (bossDef != null &&
                IsMidBossId(bossDef.Id) &&
                _presentationSkin != null &&
                _presentationSkin.OverrideMidBossTint)
            {
                return _presentationSkin.MidBossTint;
            }

            if (_presentationSkin != null && _presentationSkin.OverrideBossTint)
            {
                return _presentationSkin.BossTint;
            }

            bool hasSpriteOverride = _presentationSkin != null &&
                ((_presentationSkin.BossSprite != null) ||
                 (bossDef != null && IsMidBossId(bossDef.Id) && _presentationSkin.MidBossSprite != null));
            return hasSpriteOverride
                ? Color.white
                : (bossDef != null ? bossDef.TintColor : Color.white);
        }

        private bool IsMidBossId(string bossId)
        {
            if (string.IsNullOrWhiteSpace(bossId) || _content == null || _content.BossEncounters == null)
            {
                return false;
            }

            BossEncounterDef mid = _content.BossEncounters
                .Where(encounter => encounter != null && !encounter.IsFinalBoss)
                .OrderBy(encounter => encounter.SpawnAtSecond)
                .FirstOrDefault();
            return mid != null && string.Equals(mid.BossId, bossId, StringComparison.OrdinalIgnoreCase);
        }

        private Sprite ResolveDropSprite(DropType type)
        {
            if (_dropVisualLookup.TryGetValue(type, out DropVisualEntry entry) && entry.Sprite != null)
            {
                return entry.Sprite;
            }

            if (_presentationSkin != null && _presentationSkin.DropDefaultSprite != null)
            {
                return _presentationSkin.DropDefaultSprite;
            }

            return SpriteCache.WhiteSprite;
        }

        private Color ResolveDropTint(DropType type)
        {
            if (_dropVisualLookup.TryGetValue(type, out DropVisualEntry entry))
            {
                return entry.Tint;
            }

            return type switch
            {
                DropType.Gold => new Color(0.95f, 0.82f, 0.26f),
                DropType.Experience => new Color(0.45f, 0.95f, 0.95f),
                DropType.Core => new Color(0.95f, 0.35f, 0.95f),
                _ => Color.white,
            };
        }

        private void TickImpactFeedback(float unscaledDelta)
        {
            if (_hitStopTimer > 0f)
            {
                _hitStopTimer = Mathf.Max(0f, _hitStopTimer - unscaledDelta);
            }

            if (_camera == null)
            {
                return;
            }

            if (_shakeTimer > 0f)
            {
                _shakeTimer = Mathf.Max(0f, _shakeTimer - unscaledDelta);
                float damping = Mathf.Clamp01(_shakeTimer / Mathf.Max(0.001f, _cameraShakeSeconds));
                float noiseTime = Time.unscaledTime * _cameraShakeFrequency;
                float x = (Mathf.PerlinNoise(_shakeSeedX, noiseTime) - 0.5f) * 2f;
                float y = (Mathf.PerlinNoise(_shakeSeedY, noiseTime) - 0.5f) * 2f;
                _camera.transform.position = _cameraBasePosition + new Vector3(x, y, 0f) * (_shakeStrength * damping);
                return;
            }

            _shakeStrength = 0f;
            if (_camera.transform.position != _cameraBasePosition)
            {
                _camera.transform.position = _cameraBasePosition;
            }
        }

        private void TickAlertFlash(float deltaTime)
        {
            if (_alertFlashOverlay == null)
            {
                return;
            }

            if (_alertFlashTimer > 0f)
            {
                _alertFlashTimer = Mathf.Max(0f, _alertFlashTimer - deltaTime);
                float normalized = _alertFlashDuration > 0.001f
                    ? Mathf.Clamp01(_alertFlashTimer / _alertFlashDuration)
                    : 0f;
                Color flashColor = _alertFlashColor;
                flashColor.a *= normalized * normalized;
                _alertFlashOverlay.color = flashColor;
                return;
            }

            if (_alertFlashOverlay.color.a > 0f)
            {
                _alertFlashOverlay.color = Color.Lerp(_alertFlashOverlay.color, Color.clear, deltaTime * 10f);
            }
        }

        private void TriggerAlertFlash(int priority)
        {
            if (_alertFlashOverlay == null || priority <= 0)
            {
                return;
            }

            Color color = priority switch
            {
                3 => new Color(1f, 0.24f, 0.16f, 0.23f),
                2 => new Color(1f, 0.55f, 0.16f, 0.19f),
                _ => new Color(0.98f, 0.84f, 0.32f, 0.13f),
            };
            float duration = priority switch
            {
                3 => 0.45f,
                2 => 0.34f,
                _ => 0.24f,
            };

            if (_alertFlashTimer > 0f && _alertFlashColor.a > color.a && duration < _alertFlashDuration)
            {
                return;
            }

            _alertFlashColor = color;
            _alertFlashDuration = duration;
            _alertFlashTimer = duration;
            _alertFlashOverlay.color = color;
        }

        private void TriggerImpactFeedback(bool killed, bool crit, bool isBoss)
        {
            float hitStop = isBoss ? _bossHitStopSeconds : _hitStopSeconds;
            if (crit)
            {
                hitStop = Mathf.Max(hitStop, _critHitStopSeconds);
            }

            if (killed)
            {
                hitStop *= 1.2f;
            }

            float hitStopMultiplier = _presentationSkin != null
                ? Mathf.Max(0f, _presentationSkin.HitStopMultiplier)
                : 1f;
            hitStop *= hitStopMultiplier;

            if (!EnableAutoPilotForTests)
            {
                _hitStopTimer = Mathf.Max(_hitStopTimer, Mathf.Max(0f, hitStop));
            }

            float shakeScale = isBoss ? 1.55f : 1f;
            if (crit)
            {
                shakeScale += 0.25f;
            }

            if (killed)
            {
                shakeScale += 0.4f;
            }

            TriggerCameraShake(shakeScale);
            PlayImpactSfx(killed, crit, isBoss);
        }

        private void TriggerCameraShake(float scale)
        {
            _shakeTimer = Mathf.Max(_shakeTimer, _cameraShakeSeconds);
            float shakeMultiplier = _presentationSkin != null
                ? Mathf.Max(0f, _presentationSkin.CameraShakeMultiplier)
                : 1f;
            _shakeStrength = Mathf.Max(_shakeStrength, _cameraShakeAmplitude * shakeMultiplier * Mathf.Max(0.2f, scale));
        }

        private void PlayImpactSfx(bool killed, bool crit, bool isBoss)
        {
            if (_presentationSkin == null)
            {
                return;
            }

            if (isBoss && killed)
            {
                PlayClip(_presentationSkin.BossDefeatSfx, 1f, 0f);
                return;
            }

            if (isBoss)
            {
                PlayClip(_presentationSkin.BossHitSfx, 0.9f, 0.04f);
                return;
            }

            if (killed)
            {
                PlayClip(_presentationSkin.KillSfx, 1f, 0.06f);
                return;
            }

            if (crit)
            {
                PlayClip(_presentationSkin.CritSfx, 0.9f, 0.08f);
                return;
            }

            PlayClip(_presentationSkin.HitSfx, 0.8f, 0.08f);
        }

        private void PlayClip(AudioClip clip, float volume, float pitchJitter)
        {
            if (_sfxSource == null || clip == null)
            {
                return;
            }

            float volumeMultiplier = _presentationSkin != null
                ? Mathf.Max(0f, _presentationSkin.SfxVolumeMultiplier)
                : 1f;
            float settingsVolume = _clientSettings != null ? Mathf.Clamp01(_clientSettings.SfxVolume) : 1f;
            _sfxSource.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
            _sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume * volumeMultiplier * settingsVolume));
            _sfxSource.pitch = 1f;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystem);
        }

        private void BuildWorld()
        {
            _worldRoot = new GameObject("DemoWorld").transform;
            _dropRoot = new GameObject("Drops").transform;
            _dropRoot.SetParent(_worldRoot, false);

            GameObject background = new GameObject("Background");
            background.transform.SetParent(_worldRoot, false);
            SpriteRenderer bgRenderer = background.AddComponent<SpriteRenderer>();
            Sprite backgroundSprite = ResolveBackgroundSprite();
            bgRenderer.sprite = backgroundSprite;
            bool hasBackgroundArt = _presentationSkin != null && _presentationSkin.BackgroundSprite != null;
            bgRenderer.color = hasBackgroundArt ? Color.white : ResolveBackgroundColor();
            bgRenderer.sortingOrder = -100;
            background.transform.localPosition = new Vector3(0f, 0f, 0f);
            FitBackgroundToCamera(background.transform, bgRenderer);

            BuildHoles();
            _bossLookup.Clear();
            for (int i = 0; i < _content.Bosses.Count; i++)
            {
                BossDef def = _content.Bosses[i];
                if (def == null || string.IsNullOrWhiteSpace(def.Id))
                {
                    continue;
                }

                BossRuntime runtime = new BossRuntime(def, _worldRoot, ResolveBossSprite(def), ResolveBossTint(def));
                runtime.Deactivate();
                _bossLookup[def.Id] = runtime;
            }

            _boss = null;
            InitializeRound7VisualFeedbackWorld();
        }

        private void FitBackgroundToCamera(Transform backgroundTransform, SpriteRenderer renderer)
        {
            if (backgroundTransform == null || renderer == null || renderer.sprite == null || _camera == null)
            {
                if (backgroundTransform != null)
                {
                    backgroundTransform.localScale = new Vector3(18f, 13f, 1f);
                }

                return;
            }

            Vector2 spriteSize = renderer.sprite.bounds.size;
            float spriteWidth = Mathf.Max(0.001f, spriteSize.x);
            float spriteHeight = Mathf.Max(0.001f, spriteSize.y);
            float targetHeight = _camera.orthographicSize * 2f + 0.8f;
            float targetWidth = targetHeight * Mathf.Max(1f, _camera.aspect) + 0.8f;
            float scaleByWidth = targetWidth / spriteWidth;
            float scaleByHeight = targetHeight / spriteHeight;
            float uniformScale = Mathf.Max(scaleByWidth, scaleByHeight);
            backgroundTransform.localScale = new Vector3(uniformScale, uniformScale, 1f);
        }

        private void BuildHoles()
        {
            _holes.Clear();
            const int cols = 6;
            const int rows = 4;
            float startX = -4.8f;
            float startY = 2.8f;
            float spacingX = 1.92f;
            float spacingY = 1.58f;

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    int index = row * cols + col;
                    float offsetX = row % 2 == 0 ? 0f : 0.4f;
                    Vector2 pos = new Vector2(startX + col * spacingX + offsetX, startY - row * spacingY);

                    GameObject holeGo = new GameObject($"Hole_{index}");
                    holeGo.transform.SetParent(_worldRoot, false);
                    holeGo.transform.localPosition = pos;

                    SpriteRenderer holeRenderer = holeGo.AddComponent<SpriteRenderer>();
                    holeRenderer.sprite = ResolveHoleSprite();
                    holeRenderer.sortingOrder = 1;
                    holeGo.transform.localScale = new Vector3(1.05f, 0.44f, 1f);

                    GameObject holeCoreGo = new GameObject("HoleCore");
                    holeCoreGo.transform.SetParent(holeGo.transform, false);
                    holeCoreGo.transform.localPosition = new Vector3(0f, -0.03f, 0f);
                    SpriteRenderer holeCoreRenderer = holeCoreGo.AddComponent<SpriteRenderer>();
                    holeCoreRenderer.sprite = SpriteCache.HoleCoreSprite;
                    holeCoreRenderer.sortingOrder = 0;
                    holeCoreRenderer.color = new Color(0.03f, 0.04f, 0.05f, 0.85f);
                    holeCoreGo.transform.localScale = new Vector3(1.06f, 0.72f, 1f);

                    GameObject holeFrontGo = new GameObject("HoleFront");
                    holeFrontGo.transform.SetParent(holeGo.transform, false);
                    holeFrontGo.transform.localPosition = new Vector3(0f, -0.02f, 0f);
                    SpriteRenderer holeFrontRenderer = holeFrontGo.AddComponent<SpriteRenderer>();
                    holeFrontRenderer.sprite = ResolveHoleForegroundSprite();
                    holeFrontRenderer.sortingOrder = 4;
                    holeFrontRenderer.color = new Color(0.92f, 0.94f, 0.98f, 0.95f);
                    holeFrontGo.transform.localScale = new Vector3(1.08f, 0.56f, 1f);

                    GameObject moleGo = new GameObject("Mole");
                    moleGo.transform.SetParent(holeGo.transform, false);
                    moleGo.transform.localPosition = new Vector3(0f, 0.14f, 0f);
                    SpriteRenderer moleRenderer = moleGo.AddComponent<SpriteRenderer>();
                    moleRenderer.sprite = ResolveDefaultMoleSprite();
                    moleRenderer.sortingOrder = 3;

                    GameObject hpGo = new GameObject("MoleHp");
                    hpGo.transform.SetParent(holeGo.transform, false);
                    hpGo.transform.localPosition = new Vector3(0f, 0.78f, 0f);
                    TextMesh hpText = hpGo.AddComponent<TextMesh>();
                    hpText.anchor = TextAnchor.MiddleCenter;
                    hpText.alignment = TextAlignment.Center;
                    hpText.characterSize = 0.2f;
                    hpText.fontSize = 42;
                    hpText.color = Color.white;
                    hpGo.SetActive(false);

                    GameObject facilityGo = new GameObject("FacilityLabel");
                    facilityGo.transform.SetParent(holeGo.transform, false);
                    facilityGo.transform.localPosition = new Vector3(0f, -0.48f, 0f);
                    TextMesh facilityText = facilityGo.AddComponent<TextMesh>();
                    facilityText.anchor = TextAnchor.MiddleCenter;
                    facilityText.alignment = TextAlignment.Center;
                    facilityText.characterSize = 0.12f;
                    facilityText.fontSize = 46;
                    facilityText.color = new Color(0.8f, 0.88f, 0.96f);
                    facilityGo.SetActive(false);

                    GameObject lockGo = new GameObject("HoleLock");
                    lockGo.transform.SetParent(holeGo.transform, false);
                    lockGo.transform.localPosition = new Vector3(0f, -0.04f, 0f);
                    TextMesh lockText = lockGo.AddComponent<TextMesh>();
                    lockText.anchor = TextAnchor.MiddleCenter;
                    lockText.alignment = TextAlignment.Center;
                    lockText.characterSize = 0.12f;
                    lockText.fontSize = 46;
                    lockText.color = new Color(0.96f, 0.9f, 0.62f, 0.9f);
                    lockGo.SetActive(false);

                    float spawnWeight = 1f + UnityEngine.Random.Range(0f, 0.45f);
                    int danger = row + 1;
                    HoleRuntime hole = new HoleRuntime(
                        index,
                        pos,
                        spawnWeight,
                        danger,
                        holeRenderer,
                        moleRenderer,
                        hpText,
                        facilityText,
                        lockText,
                        _presentationSkin,
                        _moleVisualLookup);
                    _holes.Add(hole);
                }
            }
        }

        private void BuildUI()
        {
            GameObject canvasGo = new GameObject("DemoCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            BuildAlertOverlay(canvasGo.transform);

            RectTransform topBar = CreateHudChrome(
                "TopHudBar",
                canvasGo.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(24f, -74f),
                new Vector2(-24f, -10f),
                new Color(0.04f, 0.08f, 0.1f, 0.58f));
            RectTransform rightBar = CreateHudChrome(
                "RightHudBar",
                canvasGo.transform,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-380f, -420f),
                new Vector2(-18f, -86f),
                new Color(0.04f, 0.08f, 0.1f, 0.56f));
            RectTransform bottomBar = CreateHudChrome(
                "BottomHudBar",
                canvasGo.transform,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(18f, 16f),
                new Vector2(-18f, 178f),
                new Color(0.04f, 0.08f, 0.1f, 0.58f));

            _topHud = CreateText(
                "TopHud",
                topBar,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                36,
                TextAnchor.MiddleCenter,
                Color.white);
            _topHud.rectTransform.sizeDelta = new Vector2(1500f, 120f);

            _rightHud = CreateText(
                "RightHud",
                rightBar,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-16f, -154f),
                22,
                TextAnchor.UpperRight,
                new Color(0.95f, 0.95f, 0.9f));
            _rightHud.rectTransform.sizeDelta = new Vector2(360f, 400f);

            _bottomHud = CreateText(
                "BottomHud",
                bottomBar,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 0f),
                new Vector2(16f, 10f),
                22,
                TextAnchor.LowerLeft,
                new Color(0.88f, 0.96f, 0.9f));
            _bottomHud.rectTransform.offsetMin = new Vector2(16f, 12f);
            _bottomHud.rectTransform.offsetMax = new Vector2(-16f, -10f);
            _bottomHud.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bottomHud.verticalOverflow = VerticalWrapMode.Overflow;

            _centerMessage = CreateText(
                "CenterMessage",
                canvasGo.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f),
                36,
                TextAnchor.MiddleCenter,
                new Color(1f, 0.93f, 0.55f));
            _centerMessage.rectTransform.sizeDelta = new Vector2(1500f, 220f);
            Outline centerOutline = _centerMessage.gameObject.AddComponent<Outline>();
            centerOutline.effectColor = new Color(0f, 0f, 0f, 0.72f);
            centerOutline.effectDistance = new Vector2(2f, -2f);

            BuildHudMeters(canvasGo.transform, rightBar);
            BuildUpgradePanel(canvasGo.transform);
            BuildEventPanel(canvasGo.transform);
            BuildEndPanel(canvasGo.transform);
            BuildMetaPanel(canvasGo.transform);
            BuildGameFlowPanels(canvasGo.transform);
            BuildRound7HudExtensions(canvasGo.transform, rightBar, bottomBar);
        }

        private RectTransform CreateHudChrome(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            Color color)
        {
            GameObject bar = new GameObject(name, typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(parent, false);
            RectTransform rect = bar.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            Image image = bar.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private void BuildAlertOverlay(Transform parent)
        {
            GameObject overlayGo = new GameObject("AlertFlash", typeof(RectTransform), typeof(Image));
            overlayGo.transform.SetParent(parent, false);
            RectTransform rect = overlayGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _alertFlashOverlay = overlayGo.GetComponent<Image>();
            _alertFlashOverlay.color = Color.clear;
            _alertFlashOverlay.raycastTarget = false;
        }

        private void BuildUpgradePanel(Transform parent)
        {
            _upgradePanel = CreatePanel("UpgradePanel", parent, new Color(0f, 0f, 0f, 0.75f));
            _upgradePanel.SetActive(false);

            _upgradeTitle = CreateText(
                "UpgradeTitle",
                _upgradePanel.transform,
                new Vector2(0.5f, 0.83f),
                new Vector2(0.5f, 0.83f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                40,
                TextAnchor.MiddleCenter,
                Color.white);
            _upgradeTitle.text = "升级三选一";

            for (int i = 0; i < 3; i++)
            {
                float offset = 280f - i * 280f;
                Button button = CreateButton($"UpgradeButton_{i}", _upgradePanel.transform, new Vector2(0.5f, 0.5f), new Vector2(980f, 220f), new Vector2(0f, offset));
                int index = i;
                button.onClick.AddListener(() => OnUpgradeSelected(index));
                _upgradeButtons[i] = button;
                _upgradeButtonTexts[i] = button.GetComponentInChildren<Text>();
                ConfigureUpgradeCardVisual(button, i);
            }
        }

        private void BuildEventPanel(Transform parent)
        {
            _eventPanel = CreatePanel("EventPanel", parent, new Color(0f, 0f, 0f, 0.8f));
            _eventPanel.SetActive(false);

            GameObject iconGo = new GameObject("EventIcon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(_eventPanel.transform, false);
            RectTransform iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 0.74f);
            iconRect.anchorMax = new Vector2(0.5f, 0.74f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(180f, 180f);
            _eventIcon = iconGo.GetComponent<Image>();
            _eventIcon.color = new Color(1f, 1f, 1f, 0.9f);
            _eventIcon.raycastTarget = false;
            _eventIcon.gameObject.SetActive(false);

            _eventText = CreateText(
                "EventText",
                _eventPanel.transform,
                new Vector2(0.5f, 0.6f),
                new Vector2(0.5f, 0.6f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 16f),
                32,
                TextAnchor.MiddleCenter,
                Color.white);

            _eventAcceptButton = CreateButton(
                "EventAccept",
                _eventPanel.transform,
                new Vector2(0.3f, 0.4f),
                new Vector2(260f, 100f),
                new Vector2(0f, -40f));
            _eventAcceptButton.GetComponentInChildren<Text>().text = "方案A";
            _eventAcceptButton.onClick.AddListener(() => ResolveEventChoice(0));
            SetButtonIcon(_eventAcceptButton, _externalUiSkin != null ? _externalUiSkin.AcceptIconSprite : null, new Color(0.88f, 1f, 0.9f));

            _eventSkipButton = CreateButton(
                "EventSkip",
                _eventPanel.transform,
                new Vector2(0.5f, 0.4f),
                new Vector2(260f, 100f),
                new Vector2(0f, -40f));
            _eventSkipButton.GetComponentInChildren<Text>().text = "方案B";
            _eventSkipButton.onClick.AddListener(() => ResolveEventChoice(1));
            SetButtonIcon(_eventSkipButton, _externalUiSkin != null ? _externalUiSkin.SkipIconSprite : null, new Color(1f, 0.86f, 0.86f));

            _eventRerollButton = CreateButton(
                "EventRerollOrSkip",
                _eventPanel.transform,
                new Vector2(0.7f, 0.4f),
                new Vector2(260f, 100f),
                new Vector2(0f, -40f));
            _eventRerollButton.GetComponentInChildren<Text>().text = "跳过";
            _eventRerollButton.onClick.AddListener(OnEventThirdButtonPressed);
            SetButtonIcon(_eventRerollButton, _externalUiSkin != null ? _externalUiSkin.SkipIconSprite : null, new Color(0.85f, 0.92f, 1f));
        }

        private void BuildEndPanel(Transform parent)
        {
            _endPanel = CreatePanel("EndPanel", parent, new Color(0f, 0f, 0f, 0.82f));
            _endPanel.SetActive(false);
            _endPanelImage = _endPanel.GetComponent<Image>();

            _endSummary = CreateText(
                "EndSummary",
                _endPanel.transform,
                new Vector2(0.5f, 0.62f),
                new Vector2(0.5f, 0.62f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                30,
                TextAnchor.MiddleCenter,
                Color.white);

            GameObject stampGo = new GameObject("ResultStamp", typeof(RectTransform), typeof(Image));
            stampGo.transform.SetParent(_endPanel.transform, false);
            RectTransform stampRect = stampGo.GetComponent<RectTransform>();
            stampRect.anchorMin = new Vector2(0.86f, 0.76f);
            stampRect.anchorMax = new Vector2(0.86f, 0.76f);
            stampRect.pivot = new Vector2(0.5f, 0.5f);
            stampRect.sizeDelta = new Vector2(228f, 228f);
            _endResultStamp = stampGo.GetComponent<Image>();
            _endResultStamp.raycastTarget = false;
            _endResultStamp.gameObject.SetActive(false);

            Button restart = CreateButton(
                "RestartButton",
                _endPanel.transform,
                new Vector2(0.42f, 0.26f),
                new Vector2(320f, 100f),
                Vector2.zero);
            restart.GetComponentInChildren<Text>().text = "再来一局";
            restart.onClick.AddListener(StartRun);
            SetButtonIcon(restart, _externalUiSkin != null ? _externalUiSkin.RestartIconSprite : null, new Color(0.9f, 0.95f, 1f));

            Button metaButton = CreateButton(
                "MetaButton",
                _endPanel.transform,
                new Vector2(0.58f, 0.26f),
                new Vector2(320f, 100f),
                Vector2.zero);
            metaButton.GetComponentInChildren<Text>().text = "工坊成长";
            metaButton.onClick.AddListener(() => SetMetaPanelVisible(true));
            SetButtonIcon(metaButton, _externalUiSkin != null ? _externalUiSkin.MetaIconSprite : null, new Color(0.95f, 0.95f, 1f));
        }

        private void BuildMetaPanel(Transform parent)
        {
            _metaPanel = CreatePanel("MetaPanel", parent, new Color(0f, 0f, 0f, 0.88f));
            _metaPanel.SetActive(false);

            _metaHeader = CreateText(
                "MetaHeader",
                _metaPanel.transform,
                new Vector2(0.5f, 0.95f),
                new Vector2(0.5f, 0.95f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                34,
                TextAnchor.MiddleCenter,
                Color.white);

            _closeMetaButton = CreateButton(
                "CloseMeta",
                _metaPanel.transform,
                new Vector2(0.92f, 0.95f),
                new Vector2(180f, 72f),
                Vector2.zero);
            _closeMetaButton.GetComponentInChildren<Text>().text = "关闭";
            _closeMetaButton.onClick.AddListener(() => SetMetaPanelVisible(false));
            SetButtonIcon(_closeMetaButton, _externalUiSkin != null ? _externalUiSkin.SkipIconSprite : null, new Color(1f, 0.9f, 0.9f));

            GameObject scrollGo = new GameObject("MetaScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(_metaPanel.transform, false);
            RectTransform scrollRect = scrollGo.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0.08f, 0.1f);
            scrollRect.anchorMax = new Vector2(0.92f, 0.88f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;
            Image scrollImage = scrollGo.GetComponent<Image>();
            scrollImage.color = new Color(1f, 1f, 1f, 0.05f);

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(scrollGo.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            Image viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            GameObject contentGo = new GameObject(
                "Content",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = new Vector2(18f, 0f);
            contentRect.offsetMax = new Vector2(-18f, 0f);

            VerticalLayoutGroup layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 10f;
            layout.padding = new RectOffset(0, 0, 10, 20);

            ContentSizeFitter fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 26f;

            _metaListRoot = contentGo.transform;
        }

        private GameObject CreatePanel(string name, Transform parent, Color color)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = panel.GetComponent<Image>();
            if (_externalUiSkin != null && _externalUiSkin.PanelBackgroundSprite != null)
            {
                image.sprite = _externalUiSkin.PanelBackgroundSprite;
                image.type = Image.Type.Sliced;
                image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(color.a));
            }
            else
            {
                image.color = color;
            }

            return panel;
        }

        private static Text CreateText(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            GameObject textGo = new GameObject(name, typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(parent, false);
            RectTransform rect = textGo.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(1300f, 400f);

            Text text = textGo.GetComponent<Text>();
            text.font = GetBuiltinUiFont();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            Shadow shadow = textGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.62f);
            shadow.effectDistance = new Vector2(1.6f, -1.6f);
            return text;
        }

        private static Font GetBuiltinUiFont()
        {
            if (_uiFont != null)
            {
                return _uiFont;
            }

            try
            {
                _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (ArgumentException)
            {
                try
                {
                    _uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                catch (ArgumentException)
                {
                    _uiFont = null;
                }
            }

            return _uiFont;
        }

        private Button CreateButton(
            string name,
            Transform parent,
            Vector2 anchor,
            Vector2 size,
            Vector2 anchoredPosition)
        {
            GameObject buttonGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(parent, false);
            RectTransform rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            Image image = buttonGo.GetComponent<Image>();
            Button button = buttonGo.GetComponent<Button>();
            if (_externalUiSkin != null && _externalUiSkin.ButtonNormalSprite != null)
            {
                Sprite normal = _externalUiSkin.ButtonNormalSprite;
                Sprite highlighted = _externalUiSkin.ButtonHighlightedSprite != null
                    ? _externalUiSkin.ButtonHighlightedSprite
                    : normal;
                Sprite pressed = _externalUiSkin.ButtonPressedSprite != null
                    ? _externalUiSkin.ButtonPressedSprite
                    : normal;
                Sprite disabled = _externalUiSkin.ButtonDisabledSprite != null
                    ? _externalUiSkin.ButtonDisabledSprite
                    : normal;

                SpriteState state = button.spriteState;
                state.highlightedSprite = highlighted;
                state.pressedSprite = pressed;
                state.selectedSprite = highlighted;
                state.disabledSprite = disabled;
                button.spriteState = state;

                image.sprite = normal;
                image.type = Image.Type.Sliced;
                image.color = new Color(1f, 1f, 1f, 0.96f);

                ColorBlock colors = button.colors;
                colors.colorMultiplier = 1f;
                colors.fadeDuration = 0.08f;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
                colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
                colors.selectedColor = colors.highlightedColor;
                colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.78f);
                button.colors = colors;
            }
            else
            {
                image.color = new Color(0.2f, 0.3f, 0.36f, 0.94f);
                ColorBlock colors = button.colors;
                colors.normalColor = image.color;
                colors.highlightedColor = new Color(0.3f, 0.4f, 0.48f, 0.95f);
                colors.pressedColor = new Color(0.15f, 0.22f, 0.29f, 0.95f);
                colors.selectedColor = colors.highlightedColor;
                colors.disabledColor = new Color(0.18f, 0.18f, 0.18f, 0.8f);
                button.colors = colors;
            }

            Text label = CreateText(
                $"{name}_Label",
                buttonGo.transform,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                25,
                TextAnchor.MiddleCenter,
                Color.white);
            label.text = name;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 14;
            label.resizeTextMaxSize = 34;
            label.rectTransform.offsetMin = new Vector2(18f, 10f);
            label.rectTransform.offsetMax = new Vector2(-18f, -10f);

            return button;
        }

        private void SetButtonIcon(Button button, Sprite iconSprite, Color iconColor)
        {
            if (button == null || iconSprite == null)
            {
                return;
            }

            RectTransform buttonRect = button.GetComponent<RectTransform>();
            if (buttonRect == null)
            {
                return;
            }

            GameObject iconGo = new GameObject($"{button.name}_Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(button.transform, false);
            RectTransform rect = iconGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            float iconSize = Mathf.Clamp(buttonRect.sizeDelta.y * 0.5f, 24f, 46f);
            rect.sizeDelta = new Vector2(iconSize, iconSize);
            rect.anchoredPosition = new Vector2(26f, 0f);

            Image icon = iconGo.GetComponent<Image>();
            icon.sprite = iconSprite;
            icon.color = iconColor;
            icon.type = Image.Type.Simple;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.rectTransform.offsetMin = new Vector2(56f, 10f);
            }
        }

        private void StartRun()
        {
            ApplyFlowStateForFreshRun();
            _endOpen = false;
            _endPanel.SetActive(false);
            _upgradeOpen = false;
            _eventOpen = false;
            _metaOpen = false;
            _upgradePanel.SetActive(false);
            _eventPanel.SetActive(false);
            _metaPanel.SetActive(false);

            ClearDrops();
            for (int i = 0; i < _holes.Count; i++)
            {
                _holes[i].ClearFacility();
                _holes[i].ResetToIdle();
                _holes[i].SetEventPressure(false);
            }

            foreach (BossRuntime bossRuntime in _bossLookup.Values)
            {
                bossRuntime.Deactivate();
            }

            _boss = null;
            _activeBossEncounter = null;
            _bossTimeline = _bossEncounterService.CreateTimeline(_content);
            _bossSpawnScale = 1f;
            _rogueHoleIndices.Clear();

            _spawner = new SpawnerState();
            _automation = new AutomationState();
            _run = new RunState
            {
                EventCooldown = Mathf.Max(5f, _content != null ? _content.InitialEventCooldownSeconds : 55f),
                WeaponId = ResolveActiveWeaponId(),
                CharacterId = ResolveActiveCharacterId(),
                FacilityOverloadThresholdCurrent = ResolveInitialFacilityOverloadThreshold(),
                StarterAutomationGrantedSecond = -1f,
                StarterAutomationPackageGiven = false,
                FirstAutomationSecond = -1f,
                MerchantVisitCount = 0,
            };

            ApplyLoadoutAndMeta();
            ConfigureRunDifficultyAndChallenge();
            ApplyRunModifierToLoadout();
            ConfigureActiveHolesAtRunStart();
            float guaranteeMin = _content != null ? Mathf.Clamp(_content.AutomationGuaranteeMinSeconds, 10f, 180f) : 35f;
            float guaranteeMax = _content != null ? Mathf.Clamp(_content.AutomationGuaranteeMaxSeconds, guaranteeMin, 220f) : 45f;
            _starterAutomationTargetSecond = Mathf.Clamp((guaranteeMin + guaranteeMax) * 0.5f, guaranteeMin, guaranteeMax);
            _run.OpeningRoute = RollOpeningRoute();
            RefreshAutomationProgress();
            _ftueService?.ResetForRun(_content, _run, true);
            _manualAttackCooldown = 0f;
            _botAttackTimer = 0f;
            _hitStopTimer = 0f;
            _shakeTimer = 0f;
            _shakeStrength = 0f;
            _rareHintCooldown = 0f;
            _earlyReliefRepairTimer = 22f;
            _bossWarningShown = false;
            _midBossWarningShown = false;
            _messagePriority = 0;
            _expBarFlashTimer = 0f;
            if (_camera != null)
            {
                _camera.transform.position = _cameraBasePosition;
            }

            _currentOffer.Clear();
            ResetRound7VisualFeedbackState();
            string artHint = _activeArtSummary.StartsWith("外部包", StringComparison.Ordinal)
                ? $"（{_activeArtSummary}）"
                : string.Empty;
            string difficultyHint = _activeDifficulty != null ? _activeDifficulty.Name : "标准";
            string challengeHint = _activeChallenge != null ? _activeChallenge.Name : "无挑战";
            string routeHint = BuildOpeningRouteLabel().Replace("开局路线: ", string.Empty);
            ShowMessage($"开始新一局：{difficultyHint} / {challengeHint}。路线建议：{routeHint}。{artHint}", 2.2f);
        }

        private string ResolveConfiguredDefaultWeaponId()
        {
            if (_content != null &&
                !string.IsNullOrWhiteSpace(_content.DefaultWeaponId) &&
                _content.Weapons.Any(weapon => weapon != null && weapon.Id == _content.DefaultWeaponId))
            {
                return _content.DefaultWeaponId;
            }

            return _content?.Weapons.FirstOrDefault(weapon => weapon != null)?.Id ?? string.Empty;
        }

        private string ResolveConfiguredDefaultCharacterId()
        {
            if (_content != null &&
                !string.IsNullOrWhiteSpace(_content.DefaultCharacterId) &&
                _content.Characters.Any(character => character != null && character.Id == _content.DefaultCharacterId))
            {
                return _content.DefaultCharacterId;
            }

            return _content?.Characters.FirstOrDefault(character => character != null)?.Id ?? string.Empty;
        }

        private void NormalizeMetaStateAgainstContent()
        {
            if (_meta == null || _content == null)
            {
                return;
            }

            if (_meta.UnlockedWeapons == null)
            {
                _meta.UnlockedWeapons = new List<string>();
            }

            if (_meta.UnlockedCharacters == null)
            {
                _meta.UnlockedCharacters = new List<string>();
            }

            HashSet<string> validWeapons = new HashSet<string>(
                _content.Weapons.Where(weapon => weapon != null).Select(weapon => weapon.Id));
            HashSet<string> validCharacters = new HashSet<string>(
                _content.Characters.Where(character => character != null).Select(character => character.Id));

            _meta.UnlockedWeapons = _meta.UnlockedWeapons
                .Where(id => !string.IsNullOrWhiteSpace(id) && validWeapons.Contains(id))
                .Distinct()
                .ToList();
            _meta.UnlockedCharacters = _meta.UnlockedCharacters
                .Where(id => !string.IsNullOrWhiteSpace(id) && validCharacters.Contains(id))
                .Distinct()
                .ToList();

            for (int i = 0; i < _content.StartupUnlockedWeaponIds.Count; i++)
            {
                string id = _content.StartupUnlockedWeaponIds[i];
                if (!string.IsNullOrWhiteSpace(id) && validWeapons.Contains(id) && !_meta.UnlockedWeapons.Contains(id))
                {
                    _meta.UnlockedWeapons.Add(id);
                }
            }

            for (int i = 0; i < _content.StartupUnlockedCharacterIds.Count; i++)
            {
                string id = _content.StartupUnlockedCharacterIds[i];
                if (!string.IsNullOrWhiteSpace(id) && validCharacters.Contains(id) && !_meta.UnlockedCharacters.Contains(id))
                {
                    _meta.UnlockedCharacters.Add(id);
                }
            }

            if (_meta.UnlockedWeapons.Count == 0)
            {
                string fallback = ResolveConfiguredDefaultWeaponId();
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    _meta.UnlockedWeapons.Add(fallback);
                }
            }

            if (_meta.UnlockedCharacters.Count == 0)
            {
                string fallback = ResolveConfiguredDefaultCharacterId();
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    _meta.UnlockedCharacters.Add(fallback);
                }
            }

            if (string.IsNullOrWhiteSpace(_meta.ActiveWeaponId) || !_meta.UnlockedWeapons.Contains(_meta.ActiveWeaponId))
            {
                _meta.ActiveWeaponId = ResolveConfiguredDefaultWeaponId();
                if (string.IsNullOrWhiteSpace(_meta.ActiveWeaponId) && _meta.UnlockedWeapons.Count > 0)
                {
                    _meta.ActiveWeaponId = _meta.UnlockedWeapons[0];
                }
            }

            if (string.IsNullOrWhiteSpace(_meta.ActiveCharacterId) || !_meta.UnlockedCharacters.Contains(_meta.ActiveCharacterId))
            {
                _meta.ActiveCharacterId = ResolveConfiguredDefaultCharacterId();
                if (string.IsNullOrWhiteSpace(_meta.ActiveCharacterId) && _meta.UnlockedCharacters.Count > 0)
                {
                    _meta.ActiveCharacterId = _meta.UnlockedCharacters[0];
                }
            }

            if (_meta.RecentRuns == null)
            {
                _meta.RecentRuns = new List<RunSummary>();
            }

            if (_meta.RecentRuns.Count > MaxRecentRunHistory)
            {
                _meta.RecentRuns = _meta.RecentRuns.Take(MaxRecentRunHistory).ToList();
            }

            if (_content.DifficultyModes != null && _content.DifficultyModes.Count > 0)
            {
                bool difficultyValid = _content.DifficultyModes.Any(def =>
                    def != null &&
                    string.Equals(def.Id, _meta.SelectedDifficultyId, StringComparison.OrdinalIgnoreCase));
                if (!difficultyValid)
                {
                    _meta.SelectedDifficultyId = !string.IsNullOrWhiteSpace(_content.DefaultDifficultyId)
                        ? _content.DefaultDifficultyId
                        : _content.DifficultyModes.FirstOrDefault(def => def != null)?.Id ?? "DIFF_NORMAL";
                }
            }
            else
            {
                _meta.SelectedDifficultyId = "DIFF_NORMAL";
            }

            if (_content.ChallengeMods != null && _content.ChallengeMods.Count > 0)
            {
                bool challengeValid = string.IsNullOrWhiteSpace(_meta.SelectedChallengeId) ||
                                      _content.ChallengeMods.Any(def =>
                                          def != null &&
                                          string.Equals(def.Id, _meta.SelectedChallengeId, StringComparison.OrdinalIgnoreCase));
                if (!challengeValid)
                {
                    _meta.SelectedChallengeId = string.Empty;
                }
            }
            else
            {
                _meta.SelectedChallengeId = string.Empty;
            }
        }

        private int ResolveInitialFacilityOverloadThreshold()
        {
            if (_content == null || _content.Facilities == null || _content.Facilities.Count == 0)
            {
                return 18;
            }

            float average = _content.Facilities
                .Where(facility => facility != null)
                .Select(facility => facility.OverloadThreshold)
                .DefaultIfEmpty(18f)
                .Average();
            return Mathf.Clamp(Mathf.RoundToInt(average), 10, 70);
        }

        private void ConfigureRunDifficultyAndChallenge()
        {
            if (_run == null)
            {
                return;
            }

            _activeDifficulty = ResolveDifficultyForRun();
            _activeChallenge = ResolveChallengeForRun();
            _activeWaveMod = null;

            _run.DifficultyId = _activeDifficulty != null ? _activeDifficulty.Id : "DIFF_NORMAL";
            _run.ChallengeId = _activeChallenge != null ? _activeChallenge.Id : string.Empty;
            _run.DifficultyHpMultiplier = Mathf.Clamp(_activeDifficulty != null ? _activeDifficulty.HpMult : 1f, 0.55f, 3f);
            _run.DifficultyThreatMultiplier = Mathf.Clamp(_activeDifficulty != null ? _activeDifficulty.ThreatMult : 1f, 0.55f, 3f);
            _run.DifficultyRewardMultiplier = Mathf.Clamp(_activeDifficulty != null ? _activeDifficulty.RewardMult : 1f, 0.55f, 3f);
            _run.DifficultyLegendBonus = Mathf.Clamp(_activeDifficulty != null ? _activeDifficulty.LegendBonus : 0f, 0f, 1.6f);
            _run.WaveModId = string.Empty;
            _run.WaveModName = string.Empty;
            _run.WaveModRemaining = 0f;
            _run.WaveThreatBonus = 0f;
            _run.WaveSpeedBonus = 0f;
            _run.WaveRareBonus = 0f;
            _run.WaveEliteBonus = 0f;
            _run.NextWaveModRollSecond = 80f;

            if (HasChallenge("CHAL_03"))
            {
                _run.DifficultyLegendBonus += 0.16f;
            }

            if (HasChallenge("CHAL_04"))
            {
                _run.WaveRareBonus += 0.18f;
            }

            if (HasChallenge("CHAL_05"))
            {
                _run.FacilityPowerMultiplier = Mathf.Min(_run.FacilityPowerMultiplier, 0.84f);
                _run.FacilityCooldownMultiplier = Mathf.Max(_run.FacilityCooldownMultiplier, 1.12f);
            }

            if (HasChallenge("CHAL_07"))
            {
                _run.DifficultyThreatMultiplier *= 1.05f;
            }

            _run.DifficultyLegendBonus = Mathf.Clamp(_run.DifficultyLegendBonus, 0f, 1.9f);
            _run.DifficultyThreatMultiplier = Mathf.Clamp(_run.DifficultyThreatMultiplier, 0.55f, 3.5f);
            _run.DifficultyHpMultiplier = Mathf.Clamp(_run.DifficultyHpMultiplier, 0.55f, 3.5f);
            _run.DifficultyRewardMultiplier = Mathf.Clamp(_run.DifficultyRewardMultiplier, 0.55f, 3.5f);
        }

        private DifficultyModeDef ResolveDifficultyForRun()
        {
            if (_content == null || _content.DifficultyModes == null || _content.DifficultyModes.Count == 0)
            {
                return null;
            }

            string selectedId = _clientSettings != null ? _clientSettings.DifficultyId : string.Empty;
            if (string.IsNullOrWhiteSpace(selectedId))
            {
                selectedId = _meta != null ? _meta.SelectedDifficultyId : string.Empty;
            }

            if (string.IsNullOrWhiteSpace(selectedId))
            {
                selectedId = _content.DefaultDifficultyId;
            }

            DifficultyModeDef selected = _content.DifficultyModes.FirstOrDefault(def =>
                def != null &&
                string.Equals(def.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
            {
                selected = _content.DifficultyModes.FirstOrDefault(def =>
                    def != null &&
                    string.Equals(def.Id, "DIFF_NORMAL", StringComparison.OrdinalIgnoreCase));
            }

            if (selected == null)
            {
                selected = _content.DifficultyModes.FirstOrDefault(def => def != null);
            }

            if (selected != null)
            {
                if (_clientSettings != null)
                {
                    _clientSettings.DifficultyId = selected.Id;
                }

                if (_meta != null)
                {
                    _meta.SelectedDifficultyId = selected.Id;
                }
            }

            return selected;
        }

        private ChallengeModDef ResolveChallengeForRun()
        {
            if (_content == null || _content.ChallengeMods == null || _content.ChallengeMods.Count == 0)
            {
                return null;
            }

            string selectedId = _clientSettings != null ? _clientSettings.ChallengeId : string.Empty;
            if (string.IsNullOrWhiteSpace(selectedId))
            {
                selectedId = _meta != null ? _meta.SelectedChallengeId : string.Empty;
            }

            if (string.IsNullOrWhiteSpace(selectedId))
            {
                selectedId = _content.DefaultChallengeId;
            }

            if (string.IsNullOrWhiteSpace(selectedId))
            {
                return null;
            }

            ChallengeModDef selected = _content.ChallengeMods.FirstOrDefault(def =>
                def != null &&
                string.Equals(def.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
            {
                return null;
            }

            if (_clientSettings != null)
            {
                _clientSettings.ChallengeId = selected.Id;
            }

            if (_meta != null)
            {
                _meta.SelectedChallengeId = selected.Id;
            }

            return selected;
        }

        private bool HasChallenge(string challengeToken)
        {
            if (string.IsNullOrWhiteSpace(challengeToken))
            {
                return false;
            }

            if (_activeChallenge != null &&
                !string.IsNullOrWhiteSpace(_activeChallenge.Id) &&
                _activeChallenge.Id.IndexOf(challengeToken, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return _run != null &&
                !string.IsNullOrWhiteSpace(_run.ChallengeId) &&
                _run.ChallengeId.IndexOf(challengeToken, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private float ResolveRunRewardMultiplier()
        {
            float reward = _run != null ? Mathf.Max(0.55f, _run.DifficultyRewardMultiplier) : 1f;
            if (_activeChallenge != null)
            {
                reward *= 1f + Mathf.Clamp(_activeChallenge.RewardBonus, 0f, 2f);
            }

            return Mathf.Clamp(reward, 0.55f, 5f);
        }

        private void ApplyRunModifierToLoadout()
        {
            if (_run == null || _run.Stats == null)
            {
                return;
            }

            if (HasChallenge("CHAL_07"))
            {
                if (_run.Stats.AutoHammerInterval > 0f)
                {
                    _run.Stats.AutoHammerInterval = Mathf.Clamp(_run.Stats.AutoHammerInterval * 1.22f, 0.16f, 2.8f);
                }

                if (_run.Stats.DroneCount > 0)
                {
                    _run.Stats.DroneCount = Mathf.Max(1, _run.Stats.DroneCount - 1);
                }
            }
        }

        private void ConfigureActiveHolesAtRunStart()
        {
            if (_run == null || _holes == null || _holes.Count == 0)
            {
                return;
            }

            int maxHoles = ResolveConfiguredMaxHoleCount();
            int startActive = Mathf.Clamp(_content != null ? _content.StartingActiveHoles : 5, 1, maxHoles);
            for (int i = 0; i < _holes.Count; i++)
            {
                bool inMap = i < maxHoles;
                bool active = inMap && i < startActive;
                _holes[i].SetActive(active);
            }

            _run.MaxHoleCount = maxHoles;
            _run.ActiveHoleCount = startActive;
            _run.HoleUnlockPurchasedCount = 0;
        }

        private int ResolveConfiguredMaxHoleCount()
        {
            if (_holes == null || _holes.Count == 0)
            {
                return 0;
            }

            if (_content == null || _content.StageMaps == null || _content.StageMaps.Count == 0)
            {
                return _holes.Count;
            }

            StageMapDef map = _content.StageMaps
                .FirstOrDefault(candidate => candidate != null && string.Equals(candidate.Id, "MAP_FACTORY", StringComparison.OrdinalIgnoreCase));
            if (map == null)
            {
                map = _content.StageMaps.FirstOrDefault(candidate => candidate != null);
            }

            int mapHoles = map != null ? Mathf.Max(1, map.HoleCount) : _holes.Count;
            if (HasChallenge("CHAL_10"))
            {
                mapHoles = Mathf.Max(4, mapHoles - 4);
            }

            return Mathf.Clamp(mapHoles, 1, _holes.Count);
        }

        private int UnlockNextActiveHoles(int count, bool countAsPurchase)
        {
            if (_run == null || _holes == null || count <= 0)
            {
                return 0;
            }

            int unlocked = 0;
            int unlockLimit = Mathf.Clamp(_run.MaxHoleCount, 0, _holes.Count);
            for (int i = 0; i < _holes.Count && unlocked < count; i++)
            {
                if (i >= unlockLimit)
                {
                    continue;
                }

                HoleRuntime hole = _holes[i];
                if (hole == null || !hole.IsLocked)
                {
                    continue;
                }

                hole.SetActive(true);
                unlocked++;
            }

            if (unlocked > 0)
            {
                _run.ActiveHoleCount = Mathf.Clamp(_run.ActiveHoleCount + unlocked, 0, unlockLimit);
                if (countAsPurchase)
                {
                    _run.HoleUnlockPurchasedCount += unlocked;
                }
            }

            return unlocked;
        }

        private string RollOpeningRoute()
        {
            float roll = _random != null ? (float)_random.NextDouble() : UnityEngine.Random.value;
            if (roll < 0.34f)
            {
                return OpeningRouteAutoTower;
            }

            if (roll < 0.68f)
            {
                return OpeningRouteChainGrid;
            }

            return OpeningRouteBountyFactory;
        }

        private FacilityType RollStarterFacilityType()
        {
            float totalWeight = 0f;
            for (int i = 0; i < StarterFacilityWeights.Length; i++)
            {
                totalWeight += Mathf.Max(0.01f, StarterFacilityWeights[i]);
            }

            if (totalWeight <= 0.001f)
            {
                return FacilityType.AutoHammerTower;
            }

            float roll = (_random != null ? (float)_random.NextDouble() : UnityEngine.Random.value) * totalWeight;
            float acc = 0f;
            for (int i = 0; i < StarterFacilityTypes.Length; i++)
            {
                acc += Mathf.Max(0.01f, StarterFacilityWeights[i]);
                if (roll <= acc)
                {
                    return StarterFacilityTypes[i];
                }
            }

            return StarterFacilityTypes[StarterFacilityTypes.Length - 1];
        }

        private int ComputeAutomationForms()
        {
            if (_run == null || _run.Stats == null)
            {
                return 0;
            }

            int forms = 0;
            if (_run.Stats.AutoHammerInterval > 0f)
            {
                forms++;
            }

            if (_run.Stats.DroneCount > 0)
            {
                forms++;
            }

            int facilities = _run.ActiveFacilityCount;
            if (facilities <= 0 && _holes != null)
            {
                facilities = _holes.Count(h => h != null && h.Facility != null);
            }

            if (facilities > 0)
            {
                forms++;
            }

            return forms;
        }

        private void RefreshAutomationProgress()
        {
            if (_run == null)
            {
                return;
            }

            _run.CurrentAutomationForms = ComputeAutomationForms();
            if (_run.CurrentAutomationForms > 0)
            {
                if (_run.FirstAutomationSecond < 0f)
                {
                    _run.FirstAutomationSecond = _run.ElapsedSeconds;
                }

                if (_run.FirstReliefSecond < 0f)
                {
                    _run.FirstReliefSecond = _run.ElapsedSeconds;
                }
            }
        }

        private bool HasRealAutomation()
        {
            if (_run == null)
            {
                return false;
            }

            int forms = ComputeAutomationForms();
            if (forms > 0)
            {
                return true;
            }

            return _run.FacilityTriggerCount > 0;
        }

        private float GetRunDurationSeconds()
        {
            if (_content == null)
            {
                return DefaultRunDurationSeconds;
            }

            return Mathf.Max(120f, _content.RunDurationSeconds);
        }

        private float GetRareHintCooldownSeconds()
        {
            if (_content == null)
            {
                return 0.9f;
            }

            return Mathf.Clamp(_content.RareHintCooldownSeconds, 0.1f, 10f);
        }

        private float GetMidBossWarningLeadSeconds()
        {
            if (_content == null)
            {
                return 15f;
            }

            return Mathf.Clamp(_content.MidBossWarningLeadSeconds, 1f, 60f);
        }

        private float GetFinalBossWarningLeadSeconds()
        {
            if (_content == null)
            {
                return 30f;
            }

            return Mathf.Clamp(_content.FinalBossWarningLeadSeconds, 1f, 90f);
        }

        private float GetBossGraceSeconds()
        {
            if (_content == null)
            {
                return DefaultBossGraceSeconds;
            }

            return Mathf.Max(10f, _content.BossGraceSeconds);
        }

        private float GetFirstUpgradeEarliestSecond()
        {
            float runDuration = GetRunDurationSeconds();
            float fallback = Mathf.Clamp(runDuration * 0.075f, 45f, 60f);
            if (_content == null)
            {
                return fallback;
            }

            return Mathf.Clamp(_content.FirstUpgradeEarliestSeconds, 10f, Mathf.Min(runDuration * 0.5f, 180f));
        }

        private float GetFirstUpgradeLatestSecond()
        {
            float earliest = GetFirstUpgradeEarliestSecond();
            float runDuration = GetRunDurationSeconds();
            float fallback = Mathf.Clamp(earliest + 12f, earliest + 4f, Mathf.Min(runDuration * 0.55f, 220f));
            if (_content == null)
            {
                return fallback;
            }

            return Mathf.Clamp(_content.FirstUpgradeLatestSeconds, earliest + 4f, Mathf.Min(runDuration * 0.6f, 240f));
        }

        private float GetEarlyCommonTtkWindowSeconds()
        {
            return _content != null
                ? Mathf.Clamp(_content.EarlyCommonTtkWindowSeconds, 10f, 120f)
                : 45f;
        }

        private float GetEarlyCommonTtkMinHits()
        {
            return _content != null
                ? Mathf.Clamp(_content.EarlyCommonTtkMinHits, 1f, 8f)
                : 2f;
        }

        private float GetEarlyCommonTtkMaxHits()
        {
            float minHits = GetEarlyCommonTtkMinHits();
            return _content != null
                ? Mathf.Clamp(_content.EarlyCommonTtkMaxHits, minHits, 10f)
                : 3f;
        }

        private float AdjustEarlyCommonTtkHpScale(float baseHpScale, MoleDef mole)
        {
            if (_run == null || _run.Stats == null || mole == null)
            {
                return baseHpScale;
            }

            if (mole.Rarity != Rarity.Common)
            {
                return baseHpScale;
            }

            float ttkWindow = GetEarlyCommonTtkWindowSeconds();
            if (_run.ElapsedSeconds > ttkWindow)
            {
                return baseHpScale;
            }

            float minHits = GetEarlyCommonTtkMinHits();
            float maxHits = GetEarlyCommonTtkMaxHits();
            float progress = Mathf.Clamp01(_run.ElapsedSeconds / Mathf.Max(1f, ttkWindow));
            float targetHits = Mathf.Lerp(maxHits, minHits, progress * 0.55f);

            float critChance = Mathf.Clamp01(_run.Stats.CritChance);
            float critDamageBonus = Mathf.Max(0f, _run.Stats.CritDamage - 1f);
            float expectedDamage = _run.Stats.Damage * (1f + critChance * critDamageBonus * 0.35f);
            float desiredHp = Mathf.Max(2f, expectedDamage * targetHits);
            float desiredScale = desiredHp / Mathf.Max(1f, mole.BaseHp);

            if (_run.EarlyCommonKillSamples >= 4 && _run.EarlyCommonManualHitAverage > 0f)
            {
                float observed = _run.EarlyCommonManualHitAverage;
                if (observed < minHits)
                {
                    desiredScale *= 1.12f;
                }
                else if (observed > maxHits)
                {
                    desiredScale *= 0.9f;
                }
            }

            float tunedScale = Mathf.Clamp(desiredScale, 1f, 4.4f);
            return Mathf.Max(baseHpScale, tunedScale);
        }

        private void ApplyConfigDrivenPresentationSettings()
        {
            if (_content == null)
            {
                return;
            }

            _hitStopSeconds = Mathf.Clamp(_content.HitStopSeconds, 0f, 0.2f);
            _critHitStopSeconds = Mathf.Clamp(_content.CritHitStopSeconds, _hitStopSeconds, 0.3f);
            _bossHitStopSeconds = Mathf.Clamp(_content.BossHitStopSeconds, _hitStopSeconds, 0.3f);
            _cameraShakeSeconds = Mathf.Clamp(_content.CameraShakeSeconds, 0f, 1f);
            _cameraShakeAmplitude = Mathf.Clamp(_content.CameraShakeAmplitude, 0f, 0.5f);
            _cameraShakeFrequency = Mathf.Clamp(_content.CameraShakeFrequency, 1f, 120f);
        }

        private float GetMidBossSpawnSecond()
        {
            if (_bossTimeline != null)
            {
                BossEncounterRuntime mid = _bossTimeline
                    .Where(encounter => encounter != null && encounter.Def != null && !encounter.Def.IsFinalBoss)
                    .OrderBy(encounter => encounter.Def.SpawnAtSecond)
                    .FirstOrDefault();
                if (mid != null)
                {
                    return mid.Def.SpawnAtSecond;
                }
            }

            if (_content?.BossEncounters != null)
            {
                BossEncounterDef mid = _content.BossEncounters
                    .Where(encounter => encounter != null && !encounter.IsFinalBoss)
                    .OrderBy(encounter => encounter.SpawnAtSecond)
                    .FirstOrDefault();
                if (mid != null)
                {
                    return mid.SpawnAtSecond;
                }
            }

            return GetRunDurationSeconds() * 0.5f;
        }

        private float GetFinalBossSpawnSecond()
        {
            if (_bossTimeline != null)
            {
                BossEncounterRuntime final = _bossTimeline
                    .Where(encounter => encounter != null && encounter.Def != null)
                    .OrderByDescending(encounter => encounter.Def.SpawnAtSecond)
                    .FirstOrDefault();
                if (final != null)
                {
                    return final.Def.SpawnAtSecond;
                }
            }

            if (_content?.BossEncounters != null && _content.BossEncounters.Count > 0)
            {
                return _content.BossEncounters
                    .Where(encounter => encounter != null)
                    .OrderByDescending(encounter => encounter.SpawnAtSecond)
                    .First()
                    .SpawnAtSecond;
            }

            return GetRunDurationSeconds();
        }

        private string ResolveActiveWeaponId()
        {
            if (_meta.UnlockedWeapons == null || _meta.UnlockedWeapons.Count == 0)
            {
                string fallback = ResolveConfiguredDefaultWeaponId();
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    _meta.UnlockedWeapons = new List<string> { fallback };
                }
            }

            if (string.IsNullOrWhiteSpace(_meta.ActiveWeaponId) || !_meta.UnlockedWeapons.Contains(_meta.ActiveWeaponId))
            {
                _meta.ActiveWeaponId = !string.IsNullOrWhiteSpace(ResolveConfiguredDefaultWeaponId()) &&
                                       _meta.UnlockedWeapons.Contains(ResolveConfiguredDefaultWeaponId())
                    ? ResolveConfiguredDefaultWeaponId()
                    : (_meta.UnlockedWeapons.Count > 0 ? _meta.UnlockedWeapons[0] : string.Empty);
            }

            return _meta.ActiveWeaponId;
        }

        private string ResolveActiveCharacterId()
        {
            if (_meta.UnlockedCharacters == null || _meta.UnlockedCharacters.Count == 0)
            {
                string fallback = ResolveConfiguredDefaultCharacterId();
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    _meta.UnlockedCharacters = new List<string> { fallback };
                }
            }

            if (string.IsNullOrWhiteSpace(_meta.ActiveCharacterId) || !_meta.UnlockedCharacters.Contains(_meta.ActiveCharacterId))
            {
                _meta.ActiveCharacterId = !string.IsNullOrWhiteSpace(ResolveConfiguredDefaultCharacterId()) &&
                                          _meta.UnlockedCharacters.Contains(ResolveConfiguredDefaultCharacterId())
                    ? ResolveConfiguredDefaultCharacterId()
                    : (_meta.UnlockedCharacters.Count > 0 ? _meta.UnlockedCharacters[0] : string.Empty);
            }

            return _meta.ActiveCharacterId;
        }

        private void ApplyLoadoutAndMeta()
        {
            WeaponDef weapon = _content.Weapons.FirstOrDefault(w => w.Id == _run.WeaponId) ?? _content.Weapons[0];
            CharacterDef character = _content.Characters.FirstOrDefault(c => c.Id == _run.CharacterId) ?? _content.Characters[0];

            PlayerCombatStats stats = new PlayerCombatStats
            {
                Damage = weapon.Damage,
                AttackInterval = weapon.AttackInterval,
                AttackRadius = weapon.AttackRadius,
                CritChance = weapon.CritChance,
                CritDamage = weapon.CritDamage,
                ChainCount = weapon.ChainCount,
                SplashRadius = weapon.SplashRadius,
                AutoAim = weapon.AutoAim,
                AutoHammerInterval = weapon.AutoHammerInterval,
                DroneCount = weapon.DroneCount,
                GoldMultiplier = 1f,
                ExpMultiplier = 1f,
                MagnetRadius = Mathf.Clamp(
                    (_content != null ? _content.AutoPickupRange : 1.5f) * 0.65f,
                    0.45f,
                    2.8f),
                BossDamageMultiplier = 1f,
            };

            stats.Damage *= character.DamageMultiplier;
            stats.AttackRadius += character.RangeBonus;
            if (stats.AutoHammerInterval > 0f)
            {
                stats.AutoHammerInterval = Mathf.Max(0.15f, stats.AutoHammerInterval / Mathf.Max(0.01f, character.AutomationMultiplier));
            }

            _run.FacilityCooldownMultiplier = Mathf.Clamp(
                _run.FacilityCooldownMultiplier / Mathf.Lerp(1f, 1.16f, Mathf.Clamp01(character.AutomationMultiplier - 1f)),
                0.55f,
                1.2f);
            _run.FacilityPowerMultiplier = Mathf.Clamp(
                _run.FacilityPowerMultiplier * Mathf.Lerp(1f, 1.12f, Mathf.Clamp01(character.AutomationMultiplier - 1f)),
                0.6f,
                4f);

            _run.Stats = stats;
            int startDurability = _content != null ? Mathf.Max(1, _content.StartingDurability) : 12;
            _run.Durability = startDurability;
            _run.MaxDurability = startDurability;
            _run.Gold = _content != null ? Mathf.Max(0, _content.StartingGold) : 0;
            _run.Experience = _content != null ? Mathf.Max(0f, _content.StartingExperience) : 0f;
            _run.EventTickets = _content != null ? Mathf.Max(0, _content.StartingEventTickets) : 0;

            int startGoldBonus = 0;
            for (int i = 0; i < _content.MetaNodes.Count; i++)
            {
                MetaNodeDef node = _content.MetaNodes[i];
                int level = MetaStateUtils.GetNodeLevel(_meta, node.Id);
                if (level <= 0)
                {
                    continue;
                }

                for (int j = 0; j < level; j++)
                {
                    ApplyMetaEffect(node, ref startGoldBonus);
                }
            }

            _run.Gold += startGoldBonus;
            _run.Durability = Mathf.Min(_run.MaxDurability, _run.Durability);
            float runScale = Mathf.Clamp(GetRunDurationSeconds() / 600f, 0.85f, 1.15f);
            _run.NextExperience = Mathf.Clamp(40f * runScale, 34f, 50f);
            if (_run.Experience >= _run.NextExperience)
            {
                _run.PendingLevelUps = Mathf.Max(1, Mathf.FloorToInt(_run.Experience / _run.NextExperience));
            }
            _run.CodexUnlockedThisRun.Add($"codex_{_run.WeaponId}");
        }

        private void ApplyMetaEffect(MetaNodeDef node, ref int startGoldBonus)
        {
            switch (node.EffectType)
            {
                case MetaEffectType.AddStartDamage:
                    _run.Stats.Damage += node.Value;
                    break;
                case MetaEffectType.AttackIntervalMultiplier:
                    _run.Stats.AttackInterval = Mathf.Max(0.1f, _run.Stats.AttackInterval * node.Value);
                    break;
                case MetaEffectType.AddStartRange:
                    _run.Stats.AttackRadius += node.Value;
                    break;
                case MetaEffectType.AddMaxDurability:
                    _run.MaxDurability += Mathf.RoundToInt(node.Value);
                    _run.Durability += Mathf.RoundToInt(node.Value);
                    break;
                case MetaEffectType.AddGoldMultiplier:
                    _run.Stats.GoldMultiplier += node.Value;
                    break;
                case MetaEffectType.AddExpMultiplier:
                    _run.Stats.ExpMultiplier += node.Value;
                    break;
                case MetaEffectType.AddStartingGold:
                    startGoldBonus += Mathf.RoundToInt(node.Value);
                    break;
                case MetaEffectType.UnlockLightningWeapon:
                {
                    string weaponId = !string.IsNullOrWhiteSpace(node.TargetId)
                        ? node.TargetId
                        : _content.SecondaryWeaponUnlockId;
                    if (!string.IsNullOrWhiteSpace(weaponId))
                    {
                        UnlockWeapon(weaponId);
                    }

                    break;
                }
                case MetaEffectType.UnlockDroneWeapon:
                {
                    string weaponId = !string.IsNullOrWhiteSpace(node.TargetId)
                        ? node.TargetId
                        : _content.TertiaryWeaponUnlockId;
                    if (!string.IsNullOrWhiteSpace(weaponId))
                    {
                        UnlockWeapon(weaponId);
                    }

                    break;
                }
                case MetaEffectType.UnlockEngineerCharacter:
                {
                    string characterId = !string.IsNullOrWhiteSpace(node.TargetId)
                        ? node.TargetId
                        : _content.SecondaryCharacterUnlockId;
                    if (!string.IsNullOrWhiteSpace(characterId))
                    {
                        UnlockCharacter(characterId);
                    }

                    break;
                }
            }
        }

        private bool HasActiveBoss()
        {
            return _boss != null && _boss.Active;
        }

        private Vector2 GetActiveBossPosition()
        {
            return HasActiveBoss()
                ? new Vector2(_boss.Root.transform.position.x, _boss.Root.transform.position.y)
                : Vector2.zero;
        }

        private bool IsFinalBossEncounter(BossEncounterRuntime encounter)
        {
            return encounter != null && encounter.Def != null && encounter.Def.IsFinalBoss;
        }

        private string GetBossCodexId(BossDef boss)
        {
            if (boss == null || string.IsNullOrWhiteSpace(boss.Id))
            {
                return "codex_boss";
            }

            return $"codex_{boss.Id}";
        }

        private static string GetEventCodexId(RunEventDef runEvent)
        {
            if (runEvent == null || string.IsNullOrWhiteSpace(runEvent.Id))
            {
                return string.Empty;
            }

            return $"codex_{runEvent.Id}";
        }

        private void TickRun(float deltaTime)
        {
            _run.ElapsedSeconds += deltaTime;
            if (!HasActiveBoss() && _run.ElapsedSeconds <= 150f)
            {
                _earlyReliefRepairTimer -= deltaTime;
                if (_earlyReliefRepairTimer <= 0f)
                {
                    if (_run.Durability < _run.MaxDurability)
                    {
                        _run.Durability = Mathf.Min(_run.MaxDurability, _run.Durability + 1);
                    }

                    float t = Mathf.Clamp01(_run.ElapsedSeconds / 150f);
                    _earlyReliefRepairTimer = Mathf.Lerp(22f, 36f, t);
                }
            }

            _run.ActiveHoleCount = _holes.Count(h => h != null && h.IsActive);
            if (_run.MaxHoleCount <= 0)
            {
                _run.MaxHoleCount = Mathf.Max(1, _holes.Count);
            }

            _run.ActiveFacilityCount = _holes.Count(h => h.Facility != null);
            RefreshAutomationProgress();
            _rareHintCooldown = Mathf.Max(0f, _rareHintCooldown - deltaTime);
            TickWaveModDirector(deltaTime);
            _run.BountyContractRemaining = Mathf.Max(0f, _run.BountyContractRemaining - deltaTime);
            _run.RogueZoneRemaining = Mathf.Max(0f, _run.RogueZoneRemaining - deltaTime);
            if (_run.RogueZoneRemaining <= 0f && _rogueHoleIndices.Count > 0)
            {
                ClearRogueHolePressure();
            }

            if (EnableAutoPilotForTests)
            {
                TickAutoPilot(deltaTime);
            }

            TickCombo(deltaTime);
            TickEvents(deltaTime);
            TrySpawnBossEncounterIfNeeded();
            TickSpawnerAndHoles(deltaTime);
            TickAutomation(deltaTime);
            float overloadBefore = _run.FacilityOverloadTimer;
            TickFacilities(deltaTime);
            _run.ActiveFacilityCount = _holes.Count(h => h.Facility != null);
            RefreshAutomationProgress();
            TickDrops(deltaTime);
            TickBoss(deltaTime);
            EnsureFirstUpgradePacing();
            TryOpenPendingUpgrade();
            CheckMilestones();
            _run.BuildIdentity = ResolveBuildIdentity();
            UpdatePacingSnapshot();
            _ftueService?.Tick(_run.ElapsedSeconds, (text, duration, priority) => ShowMessage(text, duration, priority));

            if (overloadBefore <= 0f && _run.FacilityOverloadTimer > 0f)
            {
                ShowMessage("设施超载启动", 1.1f, 1);
            }

            float midBossSpawnSecond = GetMidBossSpawnSecond();
            float finalBossSpawnSecond = GetFinalBossSpawnSecond();
            float midBossWarningLead = GetMidBossWarningLeadSeconds();
            float finalBossWarningLead = GetFinalBossWarningLeadSeconds();

            if (!_midBossWarningShown &&
                !_run.MidBossSpawned &&
                _run.ElapsedSeconds >= Mathf.Max(0f, midBossSpawnSecond - midBossWarningLead))
            {
                _midBossWarningShown = true;
                ShowMessage($"中期 Boss 预警：{Mathf.RoundToInt(midBossWarningLead)} 秒后验收", 1.35f, 2);
            }

            if (!_bossWarningShown &&
                !_run.BossSpawned &&
                _run.ElapsedSeconds >= Mathf.Max(0f, finalBossSpawnSecond - finalBossWarningLead))
            {
                _bossWarningShown = true;
                ShowMessage($"终局 Boss 预警：{Mathf.RoundToInt(finalBossWarningLead)} 秒后来袭", 1.5f, 3);
            }

            if (_run.BossSpawned &&
                !_run.BossDefeated &&
                _run.ElapsedSeconds >= GetRunDurationSeconds() + GetBossGraceSeconds())
            {
                EndRun(false, "时间耗尽，Boss未被击败。", 12);
            }
        }

        private void TickAutoPilot(float deltaTime)
        {
            _botAttackTimer -= deltaTime;
            if (_botAttackTimer > 0f)
            {
                return;
            }

            Vector2 targetPos = Vector2.zero;
            bool hasTarget = false;
            if (HasActiveBoss())
            {
                targetPos = GetActiveBossPosition();
                hasTarget = true;
            }
            else
            {
                HoleRuntime hole = _holes
                    .Where(h => h.HasLiveMole)
                    .OrderByDescending(h => h.CurrentMole.Def.GoldReward)
                    .ThenBy(h => h.CurrentMole.RemainingHp)
                    .FirstOrDefault();
                if (hole != null)
                {
                    targetPos = hole.Position;
                    hasTarget = true;
                }
            }

            if (hasTarget)
            {
                AttackAt(targetPos, AttackSource.Manual);
            }

            _botAttackTimer = Mathf.Max(0.06f, _run.Stats.AttackInterval * 0.72f);
        }

        private void HandleInput(float deltaTime)
        {
            if (HandleGameFlowOverlayInput())
            {
                return;
            }

            if (_upgradeOpen)
            {
                if (Input.GetKeyDown(KeyCode.R) && _run.EventTickets > 0)
                {
                    RerollUpgradeOfferWithTicket();
                }

                return;
            }

            if (_eventOpen)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
                {
                    ResolveEventChoice(0);
                }
                else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
                {
                    ResolveEventChoice(1);
                }
                else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
                {
                    ResolveEventChoice(2);
                }
                else if (Input.GetKeyDown(KeyCode.R))
                {
                    ResolveEventRerollOrSkip();
                }
                else if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (_merchantShopMode && _pendingEvent != null && _pendingEvent.Type == RunEventType.MerchantBoost)
                    {
                        ShowMessage("你离开了商店。", 0.85f, 1);
                        _run.EventSkipCount++;
                        CloseEventPanelAndScheduleCooldown();
                    }
                    else
                    {
                        ResolveEventChoice(2);
                    }
                }

                return;
            }

            _manualAttackCooldown -= deltaTime;

            if (Input.GetMouseButtonDown(0) && _manualAttackCooldown <= 0f)
            {
                Vector3 mouse = _camera.ScreenToWorldPoint(Input.mousePosition);
                AttackAt(new Vector2(mouse.x, mouse.y), AttackSource.Manual);
                _manualAttackCooldown = Mathf.Max(0.08f, _run.Stats.AttackInterval);
            }

            if (Input.GetKeyDown(KeyCode.M) && _endOpen)
            {
                SetMetaPanelVisible(!_metaOpen);
            }
        }

        private bool AttackAt(Vector2 worldPoint, AttackSource source)
        {
            CollectDropsNear(worldPoint, 0.62f);
            bool isManual = source == AttackSource.Manual;
            float elapsed = _run != null ? Mathf.Max(0f, _run.ElapsedSeconds) : 0f;
            float manualAssistRadius = Mathf.Lerp(0.52f, 0.64f, Mathf.Clamp01(elapsed / 180f));
            float autoAssistRadius = Mathf.Lerp(0.48f, 0.56f, Mathf.Clamp01(elapsed / 180f));
            float clickRadius = Mathf.Max(_run.Stats.AttackRadius, isManual ? manualAssistRadius : autoAssistRadius);
            if (isManual)
            {
                RegisterManualAttackVisual(worldPoint, clickRadius);
            }

            List<HoleRuntime> inRange = _holes
                .Where(h => h.IsTargetable && Vector2.Distance(h.Position, worldPoint) <= clickRadius)
                .OrderBy(h => Vector2.Distance(h.Position, worldPoint))
                .ToList();

            if (isManual)
            {
                List<HoleRuntime> visualHits = _holes
                    .Where(h => h.VisualContainsPoint(worldPoint, 0.1f))
                    .OrderBy(h => h.DistanceToVisualCenter(worldPoint))
                    .ToList();
                if (visualHits.Count > 0)
                {
                    HashSet<HoleRuntime> merged = new HashSet<HoleRuntime>(visualHits);
                    for (int i = 0; i < inRange.Count; i++)
                    {
                        merged.Add(inRange[i]);
                    }

                    inRange = merged
                        .OrderBy(h => h.DistanceToVisualCenter(worldPoint))
                        .ThenBy(h => Vector2.Distance(h.Position, worldPoint))
                        .ToList();
                }
            }

            if (inRange.Count == 0 && _run.Stats.AutoAim)
            {
                HoleRuntime fallback = _holes
                    .Where(h => h.IsTargetable)
                    .OrderBy(h => Vector2.Distance(h.Position, worldPoint))
                    .FirstOrDefault();
                if (fallback != null && Vector2.Distance(fallback.Position, worldPoint) <= clickRadius * 1.9f)
                {
                    inRange.Add(fallback);
                }
            }

            if (inRange.Count == 0 && isManual)
            {
                HoleRuntime nearest = _holes
                    .Where(h => h.IsTargetable)
                    .OrderBy(h => h.DistanceToVisualCenter(worldPoint))
                    .FirstOrDefault();
                float fallbackFactor = Mathf.Lerp(1.35f, 1.8f, Mathf.Clamp01(elapsed / 240f));
                if (nearest != null && nearest.DistanceToVisualCenter(worldPoint) <= clickRadius * fallbackFactor)
                {
                    inRange.Add(nearest);
                }
            }

            if (inRange.Count == 0)
            {
                if (HasActiveBoss())
                {
                    float bossDist = Vector2.Distance(worldPoint, GetActiveBossPosition());
                    if (bossDist <= _run.Stats.AttackRadius * 2.5f)
                    {
                        float bossDamage = ComputeDamage(source, out bool bossCrit, true);
                        DealBossDamage(bossDamage, source, bossCrit, source == AttackSource.Manual);
                        RegisterHitSuccess(source);
                        return true;
                    }
                }

                RegisterAttackMissVisual(worldPoint, source);
                bool hasTargetableMole = _holes.Any(h => h != null && h.IsTargetable);
                RegisterMiss(hasTargetableMole);
                return false;
            }

            HoleRuntime primary = inRange[0];
            float damage = ComputeDamage(source, out bool crit, false);
            DealDamageToHole(primary, damage, source, true, source == AttackSource.Manual, crit);

            if (_run.Stats.SplashRadius > 0f)
            {
                for (int i = 1; i < inRange.Count; i++)
                {
                    HoleRuntime splashTarget = inRange[i];
                    if (Vector2.Distance(primary.Position, splashTarget.Position) <= _run.Stats.SplashRadius)
                    {
                        DealDamageToHole(
                            splashTarget,
                            damage * 0.55f,
                            AttackSource.Chain,
                            false,
                            false,
                            false,
                            primary.Position);
                    }
                }
            }

            if (_run.Stats.ChainCount > 0)
            {
                List<HoleRuntime> chainTargets = _holes
                    .Where(h => h.IsTargetable && h != primary)
                    .OrderBy(h => Vector2.Distance(h.Position, primary.Position))
                    .Take(_run.Stats.ChainCount)
                    .ToList();

                for (int i = 0; i < chainTargets.Count; i++)
                {
                    DealDamageToHole(
                        chainTargets[i],
                        damage * 0.62f,
                        AttackSource.Chain,
                        false,
                        false,
                        false,
                        primary.Position);
                }
            }

            RegisterHitSuccess(source);
            return true;
        }

        private float ComputeDamage(AttackSource source, out bool crit, bool vsBoss)
        {
            float baseDamage = _run.Stats.Damage;
            float comboMultiplier = 1f + Mathf.Min(1.2f, _run.Combo * 0.02f);
            float sourceMultiplier = source switch
            {
                AttackSource.AutoHammer => 0.92f,
                AttackSource.Drone => 0.48f,
                AttackSource.Chain => 0.64f,
                _ => 1f,
            };
            if (HasChallenge("CHAL_07") && source != AttackSource.Manual)
            {
                sourceMultiplier *= 0.78f;
            }

            float final = baseDamage * comboMultiplier * sourceMultiplier;
            crit = UnityEngine.Random.value <= Mathf.Clamp01(_run.Stats.CritChance);
            if (crit)
            {
                final *= _run.Stats.CritDamage;
            }

            if (vsBoss)
            {
                final *= _run.Stats.BossDamageMultiplier;
            }

            return Mathf.Max(1f, final);
        }

        private void DealDamageToHole(
            HoleRuntime hole,
            float damage,
            AttackSource source,
            bool allowTraitEffects,
            bool triggerFeedback,
            bool crit = false,
            Vector2? feedbackLinkFrom = null)
        {
            if (hole == null || !hole.IsTargetable || hole.CurrentMole == null)
            {
                return;
            }

            MoleRuntime mole = hole.CurrentMole;
            mole.RegisterHit(source);
            DamageResult result = mole.ApplyDamage(damage);
            if (result.ShieldBroken)
            {
                ShowMessage("护盾破裂", 0.55f);
            }

            hole.RegisterHitFlash(result.Killed);
            if (triggerFeedback)
            {
                TriggerImpactFeedback(result.Killed, crit, false);
            }

            if (_combatCueService != null && (triggerFeedback || crit || result.Killed))
            {
                _combatCueService.OnHit(crit, result.Killed, false);
            }

            RegisterCombatHitVisual(
                hole.Position + new Vector2(0f, 0.1f),
                damage,
                source,
                crit,
                result.Killed,
                false,
                source == AttackSource.Facility ? ResolvePrimaryFacilityType() : default,
                feedbackLinkFrom);

            if (!result.Killed)
            {
                return;
            }

            MoleDef def = mole.Def;
            RecordEarlyCommonTtkSample(mole, def);

            _run.TotalKills++;
            if (source == AttackSource.Manual)
            {
                _run.ManualKills++;
            }
            else
            {
                _run.AutoKills++;
            }

            if (def.Rarity >= Rarity.Epic)
            {
                _run.RareKillCount++;
            }

            float runRewardMultiplier = ResolveRunRewardMultiplier();
            int goldReward = Mathf.RoundToInt(def.GoldReward * _run.Stats.GoldMultiplier * hole.GoldRewardMultiplier * runRewardMultiplier);
            if (_run.TreasureRushRemaining > 0f)
            {
                goldReward = Mathf.RoundToInt(goldReward * 2f);
            }

            if (_run.CurseRemaining > 0f)
            {
                goldReward = Mathf.RoundToInt(goldReward * 1.35f);
            }

            if (_run.BountyContractRemaining > 0f)
            {
                goldReward = Mathf.RoundToInt(goldReward * 1.35f);
            }

            if (_rogueHoleIndices.Contains(hole.Index))
            {
                goldReward = Mathf.RoundToInt(goldReward * 1.25f);
            }

            int expReward = Mathf.RoundToInt(def.ExpReward * _run.Stats.ExpMultiplier * Mathf.Clamp(runRewardMultiplier, 0.8f, 2.8f));
            int coreReward = def.CoreReward;

            if (def.Traits.HasFlag(MoleTrait.Chest))
            {
                goldReward += 45;
                coreReward += 2;
                if (UnityEngine.Random.value <= 0.42f)
                {
                    _run.EventTickets += 1;
                    ShowMessage("获得事件券 x1", 0.95f, 2);
                }
            }

            if (def.Traits.HasFlag(MoleTrait.Wealthy))
            {
                _run.WealthyKillCount++;
                goldReward += Mathf.Max(18, Mathf.RoundToInt(goldReward * 0.6f));
                if (UnityEngine.Random.value <= 0.28f)
                {
                    _run.EventTickets += 1;
                    ShowMessage("富裕鼠掉落事件券", 0.85f, 2);
                }
            }

            if (def.Traits.HasFlag(MoleTrait.Elite))
            {
                coreReward += 4;
                if (UnityEngine.Random.value <= 0.2f)
                {
                    _run.EventTickets += 1;
                    ShowMessage("精英掉落事件券", 0.9f, 2);
                }
            }

            if (def.Traits.HasFlag(MoleTrait.Commander))
            {
                _run.CommanderKillCount++;
                _run.BountyContractRemaining = Mathf.Max(_run.BountyContractRemaining, 8f);
                coreReward += 2;
            }

            if (def.Traits.HasFlag(MoleTrait.Legend))
            {
                _run.LegendKillCount++;
                coreReward += 6;
                _run.EventTickets += 1;
                ShowMessage("传奇目标击破：额外事件券 +1", 1.05f, 3);
            }

            SpawnDrop(DropType.Gold, goldReward, hole.Position + new Vector2(0f, 0.5f));
            SpawnDrop(DropType.Experience, expReward, hole.Position + new Vector2(0.2f, 0.35f));
            if (coreReward > 0)
            {
                SpawnDrop(DropType.Core, coreReward, hole.Position + new Vector2(-0.2f, 0.4f));
            }

            _run.CodexUnlockedThisRun.Add($"codex_{def.Id}");

            if (allowTraitEffects && mole.CanSplitOnDeath && TrySpawnSplitChild(hole, def))
            {
                ShowMessage("分裂鼠：额外子体出现", 0.8f, 2);
            }

            if (allowTraitEffects && def.Traits.HasFlag(MoleTrait.Chain))
            {
                HoleRuntime extra = _holes
                    .Where(h => h.IsTargetable)
                    .OrderBy(h => Vector2.Distance(h.Position, hole.Position))
                    .FirstOrDefault();
                if (extra != null)
                {
                    DealDamageToHole(extra, damage * 0.45f, AttackSource.Chain, false, false);
                }
            }
        }

        private void RecordEarlyCommonTtkSample(MoleRuntime mole, MoleDef def)
        {
            if (_run == null || mole == null || def == null)
            {
                return;
            }

            if (def.Rarity != Rarity.Common)
            {
                return;
            }

            float ttkWindow = GetEarlyCommonTtkWindowSeconds();
            if (_run.ElapsedSeconds > ttkWindow)
            {
                return;
            }

            if (mole.ManualHitCount <= 0)
            {
                return;
            }

            int manualHits = Mathf.Max(1, mole.ManualHitCount);
            _run.EarlyCommonKillSamples++;
            _run.EarlyCommonManualHitTotal += manualHits;
            _run.EarlyCommonManualHitAverage = _run.EarlyCommonManualHitTotal / (float)_run.EarlyCommonKillSamples;
            _run.EarlyCommonLastSampleSecond = _run.ElapsedSeconds;
        }

        private void DealBossDamage(float damage, AttackSource source, bool crit = false, bool triggerFeedback = false)
        {
            if (!HasActiveBoss())
            {
                return;
            }

            bool killed = _boss.ApplyDamage(damage);
            _run.BossDamageDone += Mathf.RoundToInt(damage);
            if (triggerFeedback)
            {
                TriggerImpactFeedback(killed, crit, true);
            }

            if (_combatCueService != null && (triggerFeedback || crit || killed))
            {
                _combatCueService.OnHit(crit, killed, true);
            }

            RegisterCombatHitVisual(
                GetActiveBossPosition() + new Vector2(0f, 0.22f),
                damage,
                source,
                crit,
                killed,
                true,
                source == AttackSource.Facility ? ResolvePrimaryFacilityType() : default);

            if (killed)
            {
                BossDef defeatedBoss = _boss.Def;
                bool finalEncounter = IsFinalBossEncounter(_activeBossEncounter);
                _run.CodexUnlockedThisRun.Add(GetBossCodexId(defeatedBoss));
                _run.CodexUnlockedThisRun.Add("codex_boss");
                float runRewardMultiplier = ResolveRunRewardMultiplier();
                int rewardGold = defeatedBoss != null
                    ? Mathf.RoundToInt(defeatedBoss.RewardGold * runRewardMultiplier)
                    : 0;
                int rewardCore = defeatedBoss != null
                    ? Mathf.RoundToInt(defeatedBoss.RewardCore * Mathf.Clamp(runRewardMultiplier, 0.8f, 2.8f))
                    : 0;
                _run.Gold += rewardGold;
                _run.CoreShards += rewardCore;
                SpawnDrop(DropType.Gold, rewardGold, _boss.Root.transform.position);
                SpawnDrop(DropType.Core, rewardCore, _boss.Root.transform.position + new Vector3(0.4f, 0.3f, 0f));

                if (_activeBossEncounter != null)
                {
                    _activeBossEncounter.Defeated = true;
                    _activeBossEncounter.ShieldActive = false;
                }

                if (finalEncounter)
                {
                    _run.BossDefeated = true;
                    EndRun(true, "终局 Boss 击败，收割成功！", 30);
                }
                else
                {
                    _run.MidBossDefeated = true;
                    _run.EventTickets += 1;
                    ShowMessage($"中期 Boss 击败！奖励 +{rewardGold}G +{rewardCore}核 +1券", 1.6f, 3);
                    _activeBossEncounter = null;
                    _boss = null;
                    _bossSpawnScale = 1f;
                    ClearRogueHolePressure();
                }
            }
        }

        private void RegisterHitSuccess(AttackSource source)
        {
            _run.Combo++;
            int activeTargets = _holes != null ? _holes.Count(h => h != null && h.IsTargetable) : 0;
            float baseWindow = _content != null
                ? Mathf.Max(0.25f, _content.ComboWindowSeconds * 1.2f)
                : 3f;
            if (HasChallenge("CHAL_09"))
            {
                baseWindow *= 0.72f;
            }

            if (activeTargets <= 1)
            {
                baseWindow *= 1.2f;
            }

            _run.ComboTimer = baseWindow;
            _run.HighestCombo = Mathf.Max(_run.HighestCombo, _run.Combo);
            _run.TotalDamageEvents++;
            if (source == AttackSource.Manual && _run.Combo % 15 == 0)
            {
                ShowMessage($"{_run.Combo} 连击", 0.8f);
            }
        }

        private void RegisterMiss(bool heavyPenalty = true)
        {
            if (_run.Combo > 0 && heavyPenalty)
            {
                _run.Combo = Mathf.Max(0, _run.Combo - 2);
            }

            float softWindow = _content != null
                ? Mathf.Max(0.55f, _content.ComboWindowSeconds * 0.65f)
                : 1.4f;
            float missWindow = _content != null
                ? Mathf.Max(0.15f, _content.ComboMissWindowSeconds * 1.35f)
                : 1.05f;
            _run.ComboTimer = heavyPenalty
                ? missWindow
                : Mathf.Max(_run.ComboTimer, softWindow);
        }

        private void TickCombo(float deltaTime)
        {
            if (_run.Combo <= 0)
            {
                return;
            }

            _run.ComboTimer -= deltaTime;
            if (_run.ComboTimer <= 0f)
            {
                _run.Combo = Mathf.Max(0, _run.Combo - 1);
                float decayTick = _content != null
                    ? Mathf.Max(0.2f, _content.ComboDecayTickSeconds * 1.6f)
                    : 0.64f;
                _run.ComboTimer = _run.Combo > 0 ? decayTick : 0f;
            }
        }

        private bool TrySpawnSplitChild(HoleRuntime sourceHole, MoleDef parentDef)
        {
            if (sourceHole == null || parentDef == null || _holes == null || _holes.Count == 0)
            {
                return false;
            }

            HoleRuntime target = _holes
                .Where(h => h != sourceHole && h.CanSpawn)
                .OrderBy(h => Vector2.Distance(h.Position, sourceHole.Position))
                .ThenBy(h => h.Index)
                .FirstOrDefault();
            if (target == null)
            {
                return false;
            }

            target.Spawn(parentDef, 0.45f, 1.35f, canSplitOnDeath: false, spawnedAtSecond: _run != null ? _run.ElapsedSeconds : 0f);
            _run.SplitSpawnCount++;
            return true;
        }

        private void ApplyCommanderPressureOnSpawn(HoleRuntime anchorHole, MoleDef def)
        {
            if (anchorHole == null || def == null)
            {
                return;
            }

            int affectCount = def.Traits.HasFlag(MoleTrait.Legend) ? 4 : 2;
            float duration = def.Traits.HasFlag(MoleTrait.Legend) ? 7.5f : 4.8f;
            List<HoleRuntime> affected = _holes
                .Where(h => h != null && h.IsActive)
                .OrderBy(h => Vector2.Distance(h.Position, anchorHole.Position))
                .ThenByDescending(h => h.DangerLevel)
                .Take(Mathf.Clamp(affectCount, 1, Mathf.Max(1, _holes.Count(h => h != null && h.IsActive))))
                .ToList();
            for (int i = 0; i < affected.Count; i++)
            {
                ApplyRogueHolePressure(affected[i], duration);
            }
        }

        private void TickSpawnerAndHoles(float deltaTime)
        {
            for (int i = 0; i < _holes.Count; i++)
            {
                _holes[i].Tick(deltaTime, OnMoleEscaped);
            }

            float earlyEase = _run != null ? Mathf.Clamp01(_run.ElapsedSeconds / 210f) : 1f;
            int maxAttempts = HasActiveBoss() ? 2 : (_run != null && _run.ElapsedSeconds < 95f ? 1 : 2);
            float paceScale = Mathf.Lerp(0.34f, 1f, earlyEase);
            float threatScale = _run != null
                ? Mathf.Clamp(
                    _run.DifficultyThreatMultiplier *
                    (1f + _run.WaveThreatBonus * 0.35f),
                    0.45f,
                    2.4f)
                : 1f;
            float spawnDelta = deltaTime * paceScale * threatScale * (HasActiveBoss() ? Mathf.Clamp(_bossSpawnScale, 0.35f, 1f) : 1f);
            for (int spawnAttempts = 0; spawnAttempts < maxAttempts; spawnAttempts++)
            {
                bool spawned = _spawnDirector.TrySpawn(
                    _content,
                    _run,
                    _spawner,
                    _holes,
                    spawnDelta,
                    _random,
                    out HoleRuntime hole,
                    out MoleDef mole);

                if (!spawned)
                {
                    break;
                }

                float hpOpen = Mathf.Lerp(2.05f, 1.25f, Mathf.Clamp01(_run.ElapsedSeconds / 95f));
                float lateScale = Mathf.Clamp01((_run.ElapsedSeconds - 170f) / 340f) * 0.62f;
                float hpScale = hpOpen + lateScale;
                float difficultyHp = Mathf.Clamp(_run.DifficultyHpMultiplier, 0.55f, 3f);
                float rarityPressure = Mathf.Max(0f, _run.DifficultyLegendBonus) * (int)mole.Rarity * 0.12f;
                float elitePressure = (mole.Traits.HasFlag(MoleTrait.Elite) || mole.Rarity >= Rarity.Epic)
                    ? Mathf.Max(0f, _run.WaveEliteBonus) * 0.25f
                    : Mathf.Max(0f, _run.WaveEliteBonus) * 0.08f;
                hpScale *= difficultyHp * (1f + rarityPressure + elitePressure);
                hpScale = AdjustEarlyCommonTtkHpScale(hpScale, mole);
                float timingScale = Mathf.Lerp(1.75f, 1f, earlyEase);
                float waveSpeedScale = Mathf.Clamp(1f + _run.WaveSpeedBonus * 0.38f, 0.55f, 1.85f);
                timingScale /= waveSpeedScale;
                hole.Spawn(mole, hpScale, timingScale, spawnedAtSecond: _run.ElapsedSeconds);

                if (mole.Traits.HasFlag(MoleTrait.Commander) || mole.Traits.HasFlag(MoleTrait.Legend))
                {
                    ApplyCommanderPressureOnSpawn(hole, mole);
                    if (_rareHintCooldown <= 0f)
                    {
                        ShowMessage(mole.Traits.HasFlag(MoleTrait.Legend) ? "传奇指挥鼠入场：全局高压" : "指挥鼠入场：邻洞压力提升", 1f, 3);
                        _rareHintCooldown = GetRareHintCooldownSeconds();
                        _run.ReadabilityAlertCount++;
                    }
                }

                if (mole.Rarity >= Rarity.Epic && _rareHintCooldown <= 0f)
                {
                    ShowMessage($"稀有目标出现：{mole.DisplayName}", 0.95f);
                    _rareHintCooldown = GetRareHintCooldownSeconds();
                }
            }
        }

        private void OnMoleEscaped(HoleRuntime hole)
        {
            if (hole.CurrentMole == null)
            {
                return;
            }

            MoleDef def = hole.CurrentMole.Def;
            int damage = def.Traits.HasFlag(MoleTrait.Bomb) ? 2 : 1;
            if (def.Traits.HasFlag(MoleTrait.Elite))
            {
                damage += 1;
            }
            if (def.Traits.HasFlag(MoleTrait.Commander))
            {
                damage += 1;
            }
            if (def.Traits.HasFlag(MoleTrait.Legend))
            {
                damage += 1;
                _run.ReadabilityAlertCount++;
            }
            if (def.Traits.HasFlag(MoleTrait.Wealthy))
            {
                damage = Mathf.Max(0, damage - 1);
            }

            // Front-load some forgiveness so the first 1-2 minutes are learnable instead of punishing.
            float survivalEase = Mathf.Clamp01(_run.ElapsedSeconds / 210f);
            float scaled = damage * Mathf.Lerp(0.45f, 1f, survivalEase);
            damage = Mathf.Max(1, Mathf.RoundToInt(scaled));

            if (_run.ElapsedSeconds < 35f && damage == 1)
            {
                damage = 0;
            }

            if (damage > 0)
            {
                _run.Durability = Mathf.Max(0, _run.Durability - damage);
            }
            _spawner.EscapedCount++;
            hole.EscapeAndRetreat();
            RegisterMiss(true);

            // Keep PlayMode fast-forward tests deterministic until boss phase starts.
            if (EnableAutoPilotForTests && !_run.BossSpawned)
            {
                int testFloor = Mathf.Max(2, Mathf.CeilToInt(_run.MaxDurability * 0.45f));
                _run.Durability = Mathf.Max(_run.Durability, testFloor);
            }

            if (_run.Durability <= 0)
            {
                EndRun(false, "农场耐久归零，防线失守。", 8);
            }
        }

        private void TickAutomation(float deltaTime)
        {
            _automationService.Tick(
                _content,
                _run,
                _automation,
                _holes,
                deltaTime,
                (hole, damage, source) => DealDamageToHole(hole, damage, source, true, false),
                HasActiveBoss,
                (damage, source) => DealBossDamage(damage, source, false, false));
        }

        private void TickFacilities(float deltaTime)
        {
            _facilityService.Tick(
                _content,
                _run,
                _holes,
                deltaTime,
                HasActiveBoss,
                (hole, damage, source) => DealDamageToHole(hole, damage, source, true, false, false),
                (damage, source) => DealBossDamage(damage, source, false, false));
            _run.ActiveFacilityCount = _holes.Count(h => h.Facility != null);
        }

        private void SpawnDrop(DropType type, int amount, Vector2 position)
        {
            if (amount <= 0)
            {
                return;
            }

            int chunkCount = Mathf.Clamp(amount / 80 + 1, 1, 5);
            int perChunk = Mathf.Max(1, amount / chunkCount);
            int remaining = amount;
            for (int i = 0; i < chunkCount; i++)
            {
                int chunk = i == chunkCount - 1 ? remaining : perChunk;
                remaining -= chunk;
                DropRuntime drop = new DropRuntime(
                    type,
                    chunk,
                    position + UnityEngine.Random.insideUnitCircle * 0.12f,
                    _dropRoot,
                    ResolveDropSprite(type),
                    ResolveDropTint(type));
                _drops.Add(drop);
            }
        }

        private void TickDrops(float deltaTime)
        {
            Vector2 magnetTarget = new Vector2(5.35f, -4.9f);
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                DropRuntime drop = _drops[i];
                Vector2 activeTarget = magnetTarget;
                float activeRadius = _run.Stats.MagnetRadius;
                bool automatedPickup = activeRadius > 0f;
                ResolveLocalFacilityMagnet(drop.Position, ref activeTarget, ref activeRadius, ref automatedPickup);

                drop.Tick(deltaTime, activeTarget, activeRadius);
                if (drop.Collected)
                {
                    _drops.RemoveAt(i);
                    continue;
                }

                if (activeRadius > 0f && Vector2.Distance(drop.Position, activeTarget) <= 0.35f)
                {
                    CollectDrop(drop, automatedPickup);
                    _drops.RemoveAt(i);
                    continue;
                }

                if (drop.ShouldExpire)
                {
                    CollectDrop(drop, true);
                    _drops.RemoveAt(i);
                }
            }
        }

        private void ResolveLocalFacilityMagnet(
            Vector2 dropPosition,
            ref Vector2 target,
            ref float radius,
            ref bool automatedPickup)
        {
            float selectedRadius = radius;
            Vector2 selectedTarget = target;
            for (int i = 0; i < _holes.Count; i++)
            {
                HoleRuntime hole = _holes[i];
                if (hole.LocalMagnetRadius <= 0f)
                {
                    continue;
                }

                float localRadius = hole.LocalMagnetRadius;
                float distance = Vector2.Distance(dropPosition, hole.Position);
                if (distance > localRadius + 0.25f)
                {
                    continue;
                }

                if (localRadius >= selectedRadius * 0.95f)
                {
                    selectedRadius = localRadius;
                    selectedTarget = hole.Position + new Vector2(0f, -0.06f);
                    automatedPickup = true;
                }
            }

            radius = selectedRadius;
            target = selectedTarget;
        }

        private void CollectDropsNear(Vector2 point, float radius)
        {
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                DropRuntime drop = _drops[i];
                if (Vector2.Distance(drop.Position, point) <= radius)
                {
                    CollectDrop(drop, false);
                    _drops.RemoveAt(i);
                }
            }
        }

        private void CollectDrop(DropRuntime drop, bool automated)
        {
            if (drop.Collected)
            {
                return;
            }

            switch (drop.Type)
            {
                case DropType.Gold:
                    _run.Gold += drop.Amount;
                    _run.PeakSingleIncome = Mathf.Max(_run.PeakSingleIncome, drop.Amount);
                    if (automated)
                    {
                        _run.AutomationGoldCollected += drop.Amount;
                    }
                    else
                    {
                        _run.ManualGoldCollected += drop.Amount;
                    }

                    break;
                case DropType.Experience:
                    AddExperience(drop.Amount);
                    break;
                case DropType.Core:
                    _run.CoreShards += drop.Amount;
                    break;
            }

            RegisterDropCollectVisual(drop, automated);
            drop.MarkCollected();
        }

        private void AddExperience(int amount)
        {
            _run.Experience += amount;
            while (_run.Experience >= _run.NextExperience)
            {
                _run.Experience -= _run.NextExperience;
                _run.Level++;
                bool openingLevels = _run.Level <= 3;
                float growth = openingLevels ? 1.085f : 1.115f;
                float additive = openingLevels ? 2.2f : 2.8f;
                _run.NextExperience = Mathf.Round(_run.NextExperience * growth + additive);
                _run.PendingLevelUps++;
                _expBarFlashTimer = Mathf.Max(_expBarFlashTimer, 0.42f);
            }
        }

        private void TryOpenPendingUpgrade()
        {
            if (_run.PendingLevelUps <= 0 || _upgradeOpen || _eventOpen || _metaOpen || _endOpen)
            {
                return;
            }

            if (_run.FirstUpgradeSecond < 0f && _run.ElapsedSeconds < GetFirstUpgradeEarliestSecond())
            {
                return;
            }

            _currentOffer = _upgradeOfferService.BuildOffer(_content, _run, _random);
            if (_currentOffer.Count == 0)
            {
                return;
            }

            _upgradeTitle.text = _run.EventTickets > 0
                ? $"升级三选一  <size=24>[R] 重构(-1券) 当前:{_run.EventTickets}</size>"
                : "升级三选一";

            for (int i = 0; i < _upgradeButtons.Length; i++)
            {
                if (i >= _currentOffer.Count)
                {
                    _upgradeButtons[i].gameObject.SetActive(false);
                    continue;
                }

                _upgradeButtons[i].gameObject.SetActive(true);
                UpgradeDef def = _currentOffer[i];
                _upgradeButtonTexts[i].text = BuildUpgradeOptionText(def);
                ApplyUpgradeCardVisual(i, def);
            }

            if (_run.FirstUpgradeSecond < 0f)
            {
                _run.FirstUpgradeSecond = _run.ElapsedSeconds;
            }

            _upgradePanel.SetActive(true);
            _upgradeOpen = true;
        }

        private string BuildUpgradeOptionText(UpgradeDef def)
        {
            if (_upgradeVisualizationService != null)
            {
                return _upgradeVisualizationService.BuildOptionText(def, _run);
            }

            if (def == null)
            {
                return "无效升级";
            }

            string category = string.IsNullOrWhiteSpace(def.Category) ? "通用" : def.Category;
            string desc = UpgradePresentationFormatter.BuildReadableDescription(def, _run);
            string preview = UpgradePresentationFormatter.BuildPreviewLine(def, _run);
            return $"{def.DisplayName}\n<size=24>{desc}</size>\n<size=20>选择后: {preview}</size>\n<size=18>[{category}]</size>";
        }

        private void OnUpgradeSelected(int index)
        {
            if (!_upgradeOpen || index < 0 || index >= _currentOffer.Count)
            {
                return;
            }

            UpgradeDef def = _currentOffer[index];
            ApplyUpgrade(def);
            _run.PendingLevelUps = Mathf.Max(0, _run.PendingLevelUps - 1);
            _upgradePanel.SetActive(false);
            _upgradeOpen = false;

            if (_run.PendingLevelUps > 0)
            {
                TryOpenPendingUpgrade();
            }
        }

        private void RerollUpgradeOfferWithTicket()
        {
            if (!_upgradeOpen || _run.EventTickets <= 0)
            {
                return;
            }

            _run.EventTickets -= 1;
            List<UpgradeDef> reroll = _upgradeOfferService.BuildOffer(_content, _run, _random);
            if (reroll == null || reroll.Count == 0)
            {
                return;
            }

            _currentOffer = reroll;
            _upgradeTitle.text = _run.EventTickets > 0
                ? $"升级三选一  <size=24>[R] 重构(-1券) 当前:{_run.EventTickets}</size>"
                : "升级三选一";
            for (int i = 0; i < _upgradeButtons.Length; i++)
            {
                if (i >= _currentOffer.Count)
                {
                    _upgradeButtons[i].gameObject.SetActive(false);
                    continue;
                }

                _upgradeButtons[i].gameObject.SetActive(true);
                UpgradeDef def = _currentOffer[i];
                _upgradeButtonTexts[i].text = BuildUpgradeOptionText(def);
                ApplyUpgradeCardVisual(i, def);
            }

            ShowMessage("消耗事件券：升级方案已重构", 1.1f, 2);
        }

        private void ApplyUpgrade(UpgradeDef def)
        {
            if (def == null)
            {
                return;
            }

            if (_run.FirstUpgradeSecond < 0f)
            {
                _run.FirstUpgradeSecond = _run.ElapsedSeconds;
            }

            UpgradeStatsSnapshot beforeSnapshot = UpgradeStatsSnapshot.Capture(_run);
            if (!_run.UpgradeStacks.ContainsKey(def.Id))
            {
                _run.UpgradeStacks[def.Id] = 0;
            }

            _run.UpgradeStacks[def.Id]++;
            for (int i = 0; i < def.Tags.Count; i++)
            {
                string tag = def.Tags[i];
                _run.BuildTags.Add(tag);
                if (!_run.TagLevels.ContainsKey(tag))
                {
                    _run.TagLevels[tag] = 0;
                }

                _run.TagLevels[tag]++;
            }

            switch (def.EffectType)
            {
                case UpgradeEffectType.AddDamage:
                    _run.Stats.Damage += def.Value;
                    break;
                case UpgradeEffectType.AttackIntervalMultiplier:
                    _run.Stats.AttackInterval = Mathf.Max(0.1f, _run.Stats.AttackInterval * def.Value);
                    break;
                case UpgradeEffectType.AddRange:
                    _run.Stats.AttackRadius += def.Value;
                    break;
                case UpgradeEffectType.AddCritChance:
                    _run.Stats.CritChance = Mathf.Clamp01(_run.Stats.CritChance + def.Value);
                    break;
                case UpgradeEffectType.AddCritDamage:
                    _run.Stats.CritDamage += def.Value;
                    break;
                case UpgradeEffectType.AddChainCount:
                    _run.Stats.ChainCount += Mathf.RoundToInt(def.Value);
                    break;
                case UpgradeEffectType.AddSplash:
                    _run.Stats.SplashRadius += def.Value;
                    break;
                case UpgradeEffectType.AddGoldMultiplier:
                    _run.Stats.GoldMultiplier += def.Value;
                    break;
                case UpgradeEffectType.AddExpMultiplier:
                    _run.Stats.ExpMultiplier += def.Value;
                    break;
                case UpgradeEffectType.UnlockAutoHammer:
                    _run.Stats.AutoHammerInterval = _run.Stats.AutoHammerInterval <= 0f
                        ? def.Value
                        : Mathf.Min(_run.Stats.AutoHammerInterval, def.Value);
                    break;
                case UpgradeEffectType.AutoHammerIntervalMultiplier:
                    if (_run.Stats.AutoHammerInterval <= 0f)
                    {
                        _run.Stats.AutoHammerInterval = 1.4f;
                    }
                    else
                    {
                        _run.Stats.AutoHammerInterval = Mathf.Max(0.12f, _run.Stats.AutoHammerInterval * def.Value);
                    }

                    break;
                case UpgradeEffectType.UnlockAutoAim:
                    _run.Stats.AutoAim = true;
                    break;
                case UpgradeEffectType.AddDroneCount:
                    _run.Stats.DroneCount += Mathf.RoundToInt(def.Value);
                    break;
                case UpgradeEffectType.AddMagnetRadius:
                    _run.Stats.MagnetRadius += def.Value;
                    break;
                case UpgradeEffectType.AddMaxDurability:
                    _run.MaxDurability += Mathf.RoundToInt(def.Value);
                    _run.Durability += Mathf.RoundToInt(def.Value);
                    break;
                case UpgradeEffectType.AddBossDamageMultiplier:
                    _run.Stats.BossDamageMultiplier += def.Value;
                    break;
                case UpgradeEffectType.DeployAutoHammerTower:
                    ApplyFacilityDeployUpgrade(FacilityType.AutoHammerTower, Mathf.RoundToInt(Mathf.Max(1f, def.Value)));
                    break;
                case UpgradeEffectType.DeploySensorHammer:
                    ApplyFacilityDeployUpgrade(FacilityType.SensorHammer, Mathf.RoundToInt(Mathf.Max(1f, def.Value)));
                    break;
                case UpgradeEffectType.DeployGoldMagnet:
                    ApplyFacilityDeployUpgrade(FacilityType.GoldMagnet, Mathf.RoundToInt(Mathf.Max(1f, def.Value)));
                    break;
                case UpgradeEffectType.DeployBountyMarker:
                    ApplyFacilityDeployUpgrade(FacilityType.BountyMarker, Mathf.RoundToInt(Mathf.Max(1f, def.Value)));
                    break;
                case UpgradeEffectType.DeployTeslaCoupler:
                    ApplyFacilityDeployUpgrade(FacilityType.TeslaCoupler, Mathf.RoundToInt(Mathf.Max(1f, def.Value)));
                    break;
                case UpgradeEffectType.DeployExecutionPlate:
                    ApplyFacilityDeployUpgrade(FacilityType.ExecutionPlate, Mathf.RoundToInt(Mathf.Max(1f, def.Value)));
                    break;
                case UpgradeEffectType.FacilityCooldownMultiplier:
                    _run.FacilityCooldownMultiplier = Mathf.Clamp(_run.FacilityCooldownMultiplier * def.Value, 0.45f, 1.2f);
                    break;
                case UpgradeEffectType.FacilityPowerMultiplier:
                    _run.FacilityPowerMultiplier = Mathf.Clamp(_run.FacilityPowerMultiplier + def.Value, 0.6f, 4f);
                    break;
                case UpgradeEffectType.FacilityOverloadThresholdMultiplier:
                    _run.FacilityOverloadThresholdCurrent = Mathf.Clamp(
                        Mathf.RoundToInt(_run.FacilityOverloadThresholdCurrent * def.Value),
                        10,
                        70);
                    break;
                case UpgradeEffectType.FacilityGoldMultiplier:
                    _run.FacilityGoldMultiplier = Mathf.Clamp(_run.FacilityGoldMultiplier + def.Value, 0.5f, 4f);
                    break;
                case UpgradeEffectType.AddActiveHole:
                {
                    int requested = Mathf.Max(1, Mathf.RoundToInt(def.Value));
                    int unlocked = UnlockNextActiveHoles(requested, true);
                    if (unlocked > 0)
                    {
                        ShowMessage($"扩洞完成：+{unlocked}（{_run.ActiveHoleCount}/{_run.MaxHoleCount}）", 0.95f, 2);
                    }

                    break;
                }
            }

            RefreshAutomationProgress();
            if (HasRealAutomation())
            {
                _run.AutomationMilestoneReached = true;
                if (_run.FirstAutomationSecond < 0f)
                {
                    _run.FirstAutomationSecond = _run.ElapsedSeconds;
                }
                if (_run.FirstReliefSecond < 0f)
                {
                    _run.FirstReliefSecond = _run.ElapsedSeconds;
                }
            }

            if (_holes.Any(h => h.Facility != null))
            {
                _run.FacilityMilestoneReached = true;
            }

            CheckEvolution();
            UpgradeStatsSnapshot afterSnapshot = UpgradeStatsSnapshot.Capture(_run);
            string deltaSummary = _upgradeVisualizationService != null
                ? _upgradeVisualizationService.BuildAppliedDeltaLine(def, beforeSnapshot, afterSnapshot, _run)
                : UpgradePresentationFormatter.BuildAppliedDeltaLine(def, beforeSnapshot, afterSnapshot, _run);
            RecordUpgradeSelection(def, deltaSummary);
            RegisterUpgradeVisualDelta(def, beforeSnapshot, afterSnapshot, deltaSummary);
            ShowMessage($"获得升级：{def.DisplayName}\n{deltaSummary}", 1.35f, 2);
        }

        private void RecordUpgradeSelection(UpgradeDef def, string deltaSummary)
        {
            if (_run == null || def == null)
            {
                return;
            }

            if (!_run.UpgradePickCounts.ContainsKey(def.Id))
            {
                _run.UpgradePickCounts[def.Id] = 0;
            }

            _run.UpgradePickCounts[def.Id]++;
            int stack = _run.UpgradePickCounts[def.Id];
            string summary = string.IsNullOrWhiteSpace(deltaSummary)
                ? UpgradePresentationFormatter.BuildPreviewLine(def, _run)
                : deltaSummary;
            string item = $"{def.DisplayName} x{stack} | {summary}";
            _run.RecentUpgradePicks.Add(item);
            const int maxHistory = 6;
            while (_run.RecentUpgradePicks.Count > maxHistory)
            {
                _run.RecentUpgradePicks.RemoveAt(0);
            }

            _run.LastUpgradeDisplayName = def.DisplayName;
            _run.LastUpgradeDeltaSummary = summary;
        }

        private void ApplyFacilityDeployUpgrade(FacilityType type, int levels)
        {
            levels = Mathf.Max(1, levels);
            if (!_run.FacilityLevels.ContainsKey(type))
            {
                _run.FacilityLevels[type] = 0;
            }

            _run.FacilityLevels[type] += levels;
            if (_run.FacilityLevels[type] <= 0)
            {
                _run.FacilityLevels[type] = 1;
            }

            bool deployed = _facilityService.TryDeployFacility(_content, _run, _holes, type, out HoleRuntime hole);
            if (deployed && hole != null)
            {
                _run.FacilityMilestoneReached = true;
                if (hole.Facility?.Def != null)
                {
                    _run.CodexUnlockedThisRun.Add($"codex_{hole.Facility.Def.Id}");
                }

                string typeName = hole.Facility?.Def != null ? hole.Facility.Def.DisplayName : "设施";
                RegisterFacilityDeploymentVisual(hole, type, _run.FacilityLevels[type]);
                ShowMessage($"{typeName} 已部署至洞口 {hole.Index + 1}", 1.1f);
            }
        }

        private void CheckEvolution()
        {
            WeaponDef weapon = _content.Weapons.FirstOrDefault(w => w.Id == _run.WeaponId);
            if (weapon == null || string.IsNullOrEmpty(weapon.EvolutionId) || _run.Evolutions.Contains(weapon.EvolutionId))
            {
                return;
            }

            for (int i = 0; i < weapon.EvolutionRequirements.Count; i++)
            {
                TagRequirement requirement = weapon.EvolutionRequirements[i];
                int level = _run.TagLevels.TryGetValue(requirement.Tag, out int value) ? value : 0;
                if (level < requirement.Level)
                {
                    return;
                }
            }

            _run.Evolutions.Add(weapon.EvolutionId);
            _run.Stats.Damage *= 1.18f;
            _run.Stats.AttackInterval = Mathf.Max(0.1f, _run.Stats.AttackInterval * 0.9f);

            bool chainFocused = weapon.ChainCount > 0 ||
                                (_run.TagLevels.TryGetValue("Chain", out int chainLevel) && chainLevel >= 3);
            bool automationFocused = weapon.DroneCount > 0 ||
                                     weapon.AutoHammerInterval > 0f ||
                                     (_run.TagLevels.TryGetValue("Automation", out int autoLevel) && autoLevel >= 3);
            bool rangeFocused = weapon.SplashRadius > 0f ||
                                (_run.TagLevels.TryGetValue("Range", out int rangeLevel) && rangeLevel >= 4);
            bool critFocused = _run.TagLevels.TryGetValue("Crit", out int critLevel) && critLevel >= 3;
            bool goldFocused = _run.TagLevels.TryGetValue("Gold", out int goldLevel) && goldLevel >= 3;

            if (chainFocused)
            {
                _run.Stats.ChainCount += 1;
                _run.Stats.AutoAim = true;
            }

            if (automationFocused)
            {
                _run.Stats.DroneCount += 1;
                _run.Stats.AutoHammerInterval = _run.Stats.AutoHammerInterval <= 0f
                    ? 1.2f
                    : Mathf.Max(0.1f, _run.Stats.AutoHammerInterval * 0.86f);
            }

            if (rangeFocused)
            {
                _run.Stats.SplashRadius += 0.32f;
            }

            if (critFocused)
            {
                _run.Stats.CritChance = Mathf.Clamp01(_run.Stats.CritChance + 0.05f);
            }

            if (goldFocused)
            {
                _run.Stats.GoldMultiplier += 0.12f;
            }

            ShowMessage($"武器进化：{weapon.DisplayName}", 1.8f);
        }

        private void TickEvents(float deltaTime)
        {
            if (_run.TreasureRushRemaining > 0f)
            {
                _run.TreasureRushRemaining -= deltaTime;
            }

            if (_run.CurseRemaining > 0f)
            {
                _run.CurseRemaining -= deltaTime;
            }

            _run.EventCooldown -= deltaTime;
            float eventUnlock = _content != null ? _content.InitialEventUnlockSeconds : 55f;
            if (_run.EventCooldown > 0f || _run.ElapsedSeconds < eventUnlock || HasActiveBoss())
            {
                return;
            }

            RunEventDef selected = SelectEventForCurrentStage(null);

            if (selected == null)
            {
                _run.EventCooldown = _content != null ? _content.EventRetryCooldownSeconds : 30f;
                return;
            }

            OpenEvent(selected);
        }

        private void OpenEvent(RunEventDef runEvent)
        {
            if (runEvent == null)
            {
                return;
            }

            _pendingEvent = runEvent;
            _eventOpen = true;
            _eventPanel.SetActive(true);
            _combatCueService?.OnEvent(runEvent.Type);
            _merchantShopMode = runEvent.Type == RunEventType.MerchantBoost;
            _merchantShopOffers.Clear();
            _merchantShopCosts.Clear();
            if (_merchantShopMode)
            {
                _run.MerchantVisitCount += 1;
                PrepareMerchantShopOffers(runEvent);
            }

            Sprite eventSprite = ResolveEventSprite(runEvent.Type);
            if (_eventIcon != null)
            {
                _eventIcon.sprite = eventSprite;
                _eventIcon.color = eventSprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
                _eventIcon.gameObject.SetActive(eventSprite != null);
            }

            if (_merchantShopMode)
            {
                RefreshMerchantEventUi(runEvent);
                if (_presentationSkin != null)
                {
                    PlayClip(_presentationSkin.EventAlertSfx, 0.92f, 0.02f);
                }

                return;
            }

            string choiceA = _eventChoiceService != null
                ? _eventChoiceService.ResolveChoiceLabel(runEvent, 0)
                : "方案A";
            string choiceB = _eventChoiceService != null
                ? _eventChoiceService.ResolveChoiceLabel(runEvent, 1)
                : "方案B";
            bool hasSecondChoice = runEvent.Choices == null || runEvent.Choices.Count > 1;
            int baseMerchantCost = runEvent.Type == RunEventType.MerchantBoost
                ? ResolveMerchantCostForChoice(runEvent, false)
                : 0;
            int riskyMerchantCost = runEvent.Type == RunEventType.MerchantBoost
                ? ResolveMerchantCostForChoice(runEvent, true)
                : 0;

            _eventAcceptButton.GetComponentInChildren<Text>().text = runEvent.Type == RunEventType.MerchantBoost
                ? $"{choiceA} (-{baseMerchantCost}G)"
                : choiceA;
            _eventSkipButton.GetComponentInChildren<Text>().text = hasSecondChoice
                ? (runEvent.Type == RunEventType.MerchantBoost ? $"{choiceB} (-{riskyMerchantCost}G)" : choiceB)
                : "无次选";
            _eventSkipButton.interactable = hasSecondChoice;
            if (_eventRerollButton != null)
            {
                _eventRerollButton.GetComponentInChildren<Text>().text = _run.EventTickets > 0
                    ? $"重构 (-1券)"
                    : "跳过";
            }

            string optionA = BuildEventChoicePreview(runEvent, 0, baseMerchantCost);
            string optionB = hasSecondChoice
                ? BuildEventChoicePreview(runEvent, 1, riskyMerchantCost)
                : "无";
            string optionC = _run.EventTickets > 0 ? "重构当前事件（消耗1券）" : "跳过本次事件";
            _eventText.text =
                $"{runEvent.DisplayName}\n" +
                $"<size=25>{runEvent.Description}</size>\n\n" +
                $"<size=22>1) {optionA}\n2) {optionB}\nR) {optionC}</size>";

            if (_presentationSkin != null)
            {
                PlayClip(_presentationSkin.EventAlertSfx, 0.92f, 0.02f);
            }
        }

        private void RefreshMerchantEventUi(RunEventDef runEvent)
        {
            if (runEvent == null)
            {
                return;
            }

            int displayCount = Mathf.Min(3, Mathf.Min(_merchantShopOffers.Count, _merchantShopCosts.Count));
            if (displayCount <= 0)
            {
                _merchantShopMode = false;
                return;
            }

            string firstLabel = displayCount > 0
                ? $"{L("LOC_UI_EVENT_SLOT", "槽位")}1 (-{_merchantShopCosts[0]}G)"
                : "槽位1";
            string secondLabel = displayCount > 1
                ? $"{L("LOC_UI_EVENT_SLOT", "槽位")}2 (-{_merchantShopCosts[1]}G)"
                : "无货";
            string thirdLabel = displayCount > 2
                ? $"{L("LOC_UI_EVENT_SLOT", "槽位")}3 (-{_merchantShopCosts[2]}G)"
                : "无货";

            _eventAcceptButton.GetComponentInChildren<Text>().text = firstLabel;
            _eventSkipButton.GetComponentInChildren<Text>().text = secondLabel;
            _eventSkipButton.interactable = displayCount > 1;
            if (_eventRerollButton != null)
            {
                _eventRerollButton.GetComponentInChildren<Text>().text = thirdLabel;
                _eventRerollButton.interactable = displayCount > 2;
            }

            string optionA = BuildMerchantOfferPreview(0);
            string optionB = BuildMerchantOfferPreview(1);
            string optionC = BuildMerchantOfferPreview(2);
            string rerollHint = _run.EventTickets > 0
                ? "R) 重构货架 (-1券)"
                : "R) 无事件券，无法重构";
            _eventText.text =
                $"{runEvent.DisplayName}\n" +
                $"<size=25>{runEvent.Description}</size>\n\n" +
                $"<size=22>1) {optionA}\n2) {optionB}\n3) {optionC}\n{rerollHint}\nEsc) 离开商店</size>";
        }

        private string BuildMerchantOfferPreview(int slotIndex)
        {
            if (slotIndex < 0 ||
                slotIndex >= _merchantShopOffers.Count ||
                slotIndex >= _merchantShopCosts.Count)
            {
                return "无货";
            }

            UpgradeDef offer = _merchantShopOffers[slotIndex];
            int cost = _merchantShopCosts[slotIndex];
            if (offer == null)
            {
                return "无货";
            }

            bool affordable = _run != null && _run.Gold >= cost;
            string readable = UpgradePresentationFormatter.BuildReadableDescription(offer, _run);
            string preview = UpgradePresentationFormatter.BuildPreviewLine(offer, _run);
            string affordText = affordable ? "可购买" : "金币不足";
            return $"{offer.DisplayName} ({cost}G,{affordText})\n<size=20>{readable} / 选择后: {preview}</size>";
        }

        private void PrepareMerchantShopOffers(RunEventDef runEvent)
        {
            _merchantShopOffers.Clear();
            _merchantShopCosts.Clear();

            if (runEvent == null || _content == null || _content.Upgrades == null || _run == null)
            {
                return;
            }

            int slotCount = 3;
            if (_content.Shops != null && _content.Shops.Count > 0)
            {
                int visit = Mathf.Max(1, _run.MerchantVisitCount);
                int index = Mathf.Clamp(visit - 1, 0, _content.Shops.Count - 1);
                ShopProfileDef profile = _content.Shops[index];
                if (profile != null)
                {
                    slotCount = Mathf.Clamp(profile.SlotCount, 1, 3);
                }
            }

            float unlockWindow = _run.ElapsedSeconds + 180f;
            List<UpgradeDef> eligible = _content.Upgrades
                .Where(def =>
                    def != null &&
                    (!_run.UpgradeStacks.ContainsKey(def.Id) || _run.UpgradeStacks[def.Id] < def.MaxStacks) &&
                    def.UnlockAtSecond <= unlockWindow &&
                    !IsPlaceholderShopUpgrade(def))
                .Distinct()
                .ToList();
            if (eligible.Count == 0)
            {
                eligible = _content.Upgrades
                    .Where(def => def != null && (!_run.UpgradeStacks.ContainsKey(def.Id) || _run.UpgradeStacks[def.Id] < def.MaxStacks))
                    .Distinct()
                    .ToList();
            }

            bool earlyVisit = _run.MerchantVisitCount <= 2;
            List<UpgradeDef> picks = eligible
                .OrderByDescending(def => ScoreMerchantOfferUpgrade(def, earlyVisit))
                .ThenBy(def => def.UnlockAtSecond)
                .ThenBy(_ => UnityEngine.Random.value)
                .Take(slotCount)
                .ToList();

            while (picks.Count < slotCount)
            {
                UpgradeDef fallback = eligible.FirstOrDefault(def => !picks.Contains(def));
                if (fallback == null)
                {
                    break;
                }

                picks.Add(fallback);
            }

            int visitCount = Mathf.Max(1, _run.MerchantVisitCount);
            int earlyMin = _content != null ? Mathf.Max(0, _content.EarlyShopMinPrice) : 5;
            int earlyMax = _content != null ? Mathf.Max(earlyMin, _content.EarlyShopMaxPrice) : 12;
            int baseCost = ResolveMerchantBaseCost(runEvent);
            for (int i = 0; i < picks.Count; i++)
            {
                UpgradeDef offer = picks[i];
                int rarityStep = offer != null ? Mathf.Clamp((int)offer.Rarity, 0, 4) : 0;
                float slotFactor = i switch
                {
                    0 => 0.85f,
                    1 => 1f,
                    _ => 1.15f,
                };
                int price = Mathf.RoundToInt(baseCost * slotFactor * (1f + rarityStep * 0.12f));
                if (visitCount <= 2)
                {
                    price = Mathf.Clamp(price, earlyMin, earlyMax);
                }

                _merchantShopOffers.Add(offer);
                _merchantShopCosts.Add(Mathf.Max(0, price));
            }

            EnsureMerchantHasAffordableOffer();
        }

        private void EnsureMerchantHasAffordableOffer()
        {
            if (_run == null || _merchantShopOffers.Count == 0 || _merchantShopCosts.Count == 0)
            {
                return;
            }

            bool hasAffordable = false;
            for (int i = 0; i < _merchantShopCosts.Count; i++)
            {
                if (_merchantShopCosts[i] <= _run.Gold)
                {
                    hasAffordable = true;
                    break;
                }
            }

            if (hasAffordable)
            {
                return;
            }

            int cheapestIndex = 0;
            for (int i = 1; i < _merchantShopCosts.Count; i++)
            {
                if (_merchantShopCosts[i] < _merchantShopCosts[cheapestIndex])
                {
                    cheapestIndex = i;
                }
            }

            _merchantShopCosts[cheapestIndex] = Mathf.Max(0, _run.Gold);
        }

        private static bool IsPlaceholderShopUpgrade(UpgradeDef def)
        {
            if (def == null)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(def.Id) &&
                def.Id.StartsWith("UPG_MISC_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string name = def.DisplayName ?? string.Empty;
            string desc = def.Description ?? string.Empty;
            return desc.IndexOf("向泛用条目", StringComparison.Ordinal) >= 0 ||
                   (name.IndexOf("升级", StringComparison.Ordinal) >= 0 && desc.IndexOf("向泛用", StringComparison.Ordinal) >= 0);
        }

        private float ScoreMerchantOfferUpgrade(UpgradeDef def, bool earlyVisit)
        {
            if (def == null || _run == null)
            {
                return -999f;
            }

            float score = def.BaseWeight + (int)def.Rarity * 0.32f;
            if (earlyVisit)
            {
                score += def.EffectType switch
                {
                    UpgradeEffectType.AddActiveHole => 5.8f,
                    UpgradeEffectType.UnlockAutoHammer => 5.2f,
                    UpgradeEffectType.AutoHammerIntervalMultiplier => 3.8f,
                    UpgradeEffectType.DeployAutoHammerTower => 4.6f,
                    UpgradeEffectType.DeploySensorHammer => 4.5f,
                    UpgradeEffectType.DeployGoldMagnet => 4.4f,
                    UpgradeEffectType.AddDroneCount => 3.2f,
                    _ => 0f,
                };
            }

            if (def.Tags.Contains("Automation"))
            {
                score += earlyVisit ? 2.2f : 1.15f;
            }

            if (def.Tags.Contains("Facility"))
            {
                score += earlyVisit ? 1.9f : 1.05f;
            }

            if (def.Tags.Contains("Expansion"))
            {
                bool canExpand = _run.ActiveHoleCount < _run.MaxHoleCount;
                score += canExpand ? 2.7f : -0.6f;
            }

            if (def.UnlockAtSecond > _run.ElapsedSeconds + 90f)
            {
                score -= 2.5f;
            }

            return score;
        }

        private void OnEventThirdButtonPressed()
        {
            if (_merchantShopMode && _pendingEvent != null && _pendingEvent.Type == RunEventType.MerchantBoost)
            {
                ResolveEventChoice(2);
                return;
            }

            ResolveEventRerollOrSkip();
        }

        private Sprite ResolveEventSprite(RunEventType type)
        {
            if (_presentationSkin == null)
            {
                return null;
            }

            return type switch
            {
                RunEventType.MerchantBoost => _presentationSkin.EventMerchantSprite,
                RunEventType.TreasureRush => _presentationSkin.EventTreasureSprite,
                RunEventType.CurseAltar => _presentationSkin.EventCurseSprite,
                RunEventType.RepairStation => _presentationSkin.EventRepairSprite,
                RunEventType.BountyContract => _presentationSkin.EventBountySprite,
                RunEventType.RogueHoleZone => _presentationSkin.EventRogueSprite,
                _ => null,
            };
        }

        private string BuildEventChoicePreview(RunEventDef runEvent, int choiceIndex, int merchantCost)
        {
            if (runEvent == null)
            {
                return "无";
            }

            bool risky = choiceIndex == 1;
            float rewardScale = ResolveEventRewardScale(runEvent, choiceIndex);
            float riskScale = ResolveEventRiskScale(runEvent, choiceIndex);
            return runEvent.Type switch
            {
                RunEventType.MerchantBoost => risky
                    ? $"付费 {merchantCost}G，获得2条升级并附带诅咒压力"
                    : $"付费 {merchantCost}G，获得1条升级（前期优先自动化）",
                RunEventType.TreasureRush => risky
                    ? $"高收益采掘 {runEvent.Value * rewardScale:0}s，并增加风险"
                    : $"稳健采掘 {runEvent.Value * rewardScale:0}s，金币收益提升",
                RunEventType.CurseAltar => risky
                    ? $"诅咒强化 {runEvent.Value * 1.25f:0}s，返还1张事件券"
                    : $"适中诅咒 {runEvent.Value * 0.9f:0}s，换取额外赏金",
                RunEventType.RepairStation => risky
                    ? $"耐久修复并超频设施（风险 {riskScale:0.00}）"
                    : $"恢复耐久并降低设施冷却",
                RunEventType.BountyContract => risky
                    ? $"高压赏金 {runEvent.Value * rewardScale:0}s + 暴走洞区"
                    : $"稳健赏金 {runEvent.Value * rewardScale:0}s，提升稀有收益",
                RunEventType.RogueHoleZone => risky
                    ? $"全线暴走 {runEvent.Value * 1.25f:0}s，高压高收益"
                    : $"局部暴走 {runEvent.Value * 0.85f:0}s，可控高压",
                _ => runEvent.Description,
            };
        }

        private void ResolveEventChoice(int choiceIndex)
        {
            if (!_eventOpen || _pendingEvent == null)
            {
                return;
            }

            if (choiceIndex < 0)
            {
                return;
            }

            bool participated = false;
            bool isMerchant = _merchantShopMode &&
                              _pendingEvent.Type == RunEventType.MerchantBoost;
            if (isMerchant)
            {
                int available = Mathf.Min(_merchantShopOffers.Count, _merchantShopCosts.Count);
                if (available <= 0)
                {
                    ShowMessage("商店暂时无货。", 0.9f, 1);
                    return;
                }

                if (choiceIndex >= available)
                {
                    ShowMessage("该槽位暂无可购买条目。", 0.9f, 1);
                    return;
                }

                int slotIndex = choiceIndex;
                bool success = ApplyEventChoice(_pendingEvent, slotIndex);
                if (!success)
                {
                    return;
                }

                participated = true;
                if (slotIndex == 0)
                {
                    _run.EventChoiceACount++;
                }
                else
                {
                    _run.EventChoiceBCount++;
                }
            }
            else
            {
                if (_pendingEvent.Choices != null &&
                    _pendingEvent.Choices.Count > 0 &&
                    choiceIndex >= _pendingEvent.Choices.Count)
                {
                    choiceIndex = Mathf.Max(0, _pendingEvent.Choices.Count - 1);
                }

                if (IsSkipChoice(_pendingEvent, choiceIndex))
                {
                    choiceIndex = 2;
                }

                if (choiceIndex == 0)
                {
                    bool success = ApplyEventChoice(_pendingEvent, 0);
                    if (!success)
                    {
                        return;
                    }

                    participated = true;
                    _run.EventChoiceACount++;
                }
                else if (choiceIndex == 1)
                {
                    bool success = ApplyEventChoice(_pendingEvent, 1);
                    if (!success)
                    {
                        return;
                    }

                    participated = true;
                    _run.EventChoiceBCount++;
                }
                else
                {
                    _run.EventSkipCount++;
                    ShowMessage("你选择了跳过事件。", 0.9f, 1);
                }
            }

            if (participated)
            {
                _run.EventParticipationCount++;
            }

            string eventCodexId = GetEventCodexId(_pendingEvent);
            if (!string.IsNullOrWhiteSpace(eventCodexId))
            {
                _run.CodexUnlockedThisRun.Add(eventCodexId);
            }

            CloseEventPanelAndScheduleCooldown();
        }

        private void ResolveEventRerollOrSkip()
        {
            if (!_eventOpen || _pendingEvent == null)
            {
                return;
            }

            if (_run.EventTickets > 0)
            {
                _run.EventTickets -= 1;
                if (_merchantShopMode && _pendingEvent.Type == RunEventType.MerchantBoost)
                {
                    PrepareMerchantShopOffers(_pendingEvent);
                    RefreshMerchantEventUi(_pendingEvent);
                    ShowMessage("商店货架已重构", 1f, 2);
                    return;
                }

                RunEventDef reroll = SelectEventForCurrentStage(_pendingEvent.Id);
                if (reroll != null)
                {
                    ShowMessage("事件已重构", 1f, 2);
                    OpenEvent(reroll);
                    return;
                }
            }

            _run.EventSkipCount++;
            ShowMessage("你选择了跳过事件。", 0.9f, 1);
            CloseEventPanelAndScheduleCooldown();
        }

        private void CloseEventPanelAndScheduleCooldown()
        {
            float progress = Mathf.Clamp01(_run.ElapsedSeconds / GetRunDurationSeconds());
            float minCooldown = Mathf.Lerp(95f, 62f, progress);
            float maxCooldown = Mathf.Lerp(130f, 92f, progress);
            _run.EventCooldown = UnityEngine.Random.Range(minCooldown, maxCooldown);
            _eventOpen = false;
            _eventPanel.SetActive(false);
            _pendingEvent = null;
            _merchantShopMode = false;
            _merchantShopOffers.Clear();
            _merchantShopCosts.Clear();
        }

        private bool ApplyEventChoice(RunEventDef runEvent, int choiceIndex)
        {
            if (runEvent == null)
            {
                return false;
            }

            bool riskyChoice = choiceIndex == 1;
            float rewardScale = ResolveEventRewardScale(runEvent, choiceIndex);
            float riskScale = ResolveEventRiskScale(runEvent, choiceIndex);
            switch (runEvent.Type)
            {
                case RunEventType.MerchantBoost:
                {
                    if (HasChallenge("CHAL_01"))
                    {
                        ShowMessage("挑战【禁商店】生效：本次交易被禁用。", 1.2f, 2);
                        return true;
                    }

                    if (_merchantShopMode && _merchantShopOffers.Count > 0 && _merchantShopCosts.Count > 0)
                    {
                        int available = Mathf.Min(_merchantShopOffers.Count, _merchantShopCosts.Count);
                        if (choiceIndex < 0 || choiceIndex >= available)
                        {
                            ShowMessage("该槽位暂无可用强化。", 0.95f, 1);
                            return false;
                        }

                        int slotIndex = choiceIndex;
                        UpgradeDef selectedOffer = _merchantShopOffers[slotIndex];
                        int cost = _merchantShopCosts[slotIndex];
                        if (selectedOffer == null)
                        {
                            ShowMessage("该槽位暂无可用强化。", 0.95f, 1);
                            return false;
                        }

                        if (_run.Gold < cost)
                        {
                            ShowMessage("金币不足，无法购买该槽位。", 1f, 2);
                            return false;
                        }

                        _run.Gold -= cost;
                        ApplyUpgrade(selectedOffer);
                        ShowMessage($"商店采购：{selectedOffer.DisplayName} (-{cost}G)", 1.15f, 2);
                        return true;
                    }

                    int legacyCost = ResolveMerchantCostForChoice(runEvent, riskyChoice);
                    if (_run.Gold < legacyCost)
                    {
                        ShowMessage("金币不足，商人拒绝交易。", 1f, 2);
                        return false;
                    }

                    _run.Gold -= legacyCost;
                    int giftCount = riskyChoice ? 2 : 1;
                    bool guaranteedGiven = false;
                    bool earlyVisit = _run.MerchantVisitCount <= 2;
                    if (earlyVisit)
                    {
                        UpgradeDef guaranteed = PickEarlyAutomationOrExpansionGift();
                        if (guaranteed != null)
                        {
                            ApplyUpgrade(guaranteed);
                            guaranteedGiven = true;
                        }
                    }

                    for (int i = 0; i < giftCount; i++)
                    {
                        UpgradeDef gift = (!guaranteedGiven && i == 0)
                            ? PickEarlyAutomationOrExpansionGift()
                            : null;
                        gift ??= PickEventGiftUpgrade(riskyChoice);
                        if (gift != null)
                        {
                            ApplyUpgrade(gift);
                            guaranteedGiven = true;
                        }
                    }

                    if (riskyChoice)
                    {
                        _run.CurseRemaining = Mathf.Max(_run.CurseRemaining, 7f + 12f * riskScale);
                    }

                    ShowMessage(riskyChoice ? "豪赌采购完成：获得双强化并承受诅咒压力" : "标准采购完成：获得额外强化", 1.3f, 2);
                    return true;
                }
                case RunEventType.TreasureRush:
                    _run.TreasureRushRemaining = Mathf.Max(_run.TreasureRushRemaining, runEvent.Value * rewardScale);
                    if (riskyChoice)
                    {
                        _run.CurseRemaining = Mathf.Max(_run.CurseRemaining, 5f + 8f * riskScale);
                    }
                    ShowMessage(riskyChoice ? "超载采掘启动：收益更高，风险同步上升" : "稳健采掘启动：短时高收益", 1.25f, 2);
                    return true;
                case RunEventType.CurseAltar:
                    _run.CurseRemaining = Mathf.Max(_run.CurseRemaining, runEvent.Value * (riskyChoice ? 1.25f : 0.9f));
                    _run.BountyContractRemaining = Mathf.Max(_run.BountyContractRemaining, runEvent.Value * 0.3f * rewardScale);
                    if (riskyChoice)
                    {
                        _run.EventTickets += 1;
                    }
                    ShowMessage(riskyChoice ? "高压赌注生效：诅咒升级并返还事件券" : "低压祭礼生效：适中风险换取收益", 1.35f, 2);
                    return true;
                case RunEventType.RepairStation:
                {
                    int heal = HasChallenge("CHAL_02")
                        ? 0
                        : Mathf.RoundToInt(runEvent.Value * (riskyChoice ? 0.72f : 1f));
                    _run.Durability = Mathf.Min(_run.MaxDurability, _run.Durability + heal);
                    bool boosted = _facilityService.BoostFacilitiesForRepair(_holes, riskyChoice ? 0.62f : 0.45f);
                    if (riskyChoice)
                    {
                        _run.FacilityPowerMultiplier = Mathf.Clamp(_run.FacilityPowerMultiplier + 0.08f + 0.08f * riskScale, 0.6f, 4f);
                    }

                    ShowMessage(HasChallenge("CHAL_02")
                        ? "挑战【无回复】生效：维修站仅提供设施维护。"
                        : boosted
                        ? (riskyChoice ? "深度检修完成：设施超频并恢复耐久" : "快速检修完成：耐久恢复并修复设施")
                        : "维修站完成：农场耐久恢复", 1.25f, 2);
                    return true;
                }
                case RunEventType.BountyContract:
                    _run.BountyContractRemaining = Mathf.Max(_run.BountyContractRemaining, runEvent.Value * rewardScale);
                    _run.BountyContractCount++;
                    if (riskyChoice)
                    {
                        ActivateRogueZone(Mathf.Clamp(6f + runEvent.Value * 0.35f, 6f, 16f), 4);
                    }
                    ShowMessage(riskyChoice ? "高压合约生效：赏金提升并叠加暴走洞区" : "稳健合约生效：稀有权重与收益提升", 1.25f, 2);
                    return true;
                case RunEventType.RogueHoleZone:
                {
                    float duration = runEvent.Value * (riskyChoice ? 1.25f : 0.85f);
                    int holeCount = riskyChoice ? 6 : 4;
                    ActivateRogueZone(duration, holeCount);
                    if (riskyChoice)
                    {
                        _run.BountyContractRemaining = Mathf.Max(_run.BountyContractRemaining, 10f * rewardScale);
                    }
                    ShowMessage(riskyChoice ? "全线暴走：高压高收益阶段启动" : "局部暴走：可控高压区形成", 1.25f, 2);
                    return true;
                }
            }

            return false;
        }

        private UpgradeDef PickEventGiftUpgrade(bool risky)
        {
            IEnumerable<UpgradeDef> pool = _content.Upgrades
                .Where(u => !_run.UpgradeStacks.ContainsKey(u.Id) || _run.UpgradeStacks[u.Id] < u.MaxStacks);
            if (risky)
            {
                pool = pool.OrderByDescending(u => u.Rarity).ThenBy(_ => UnityEngine.Random.value);
            }
            else
            {
                pool = pool.OrderByDescending(u => u.BaseWeight).ThenByDescending(u => u.Rarity).ThenBy(_ => UnityEngine.Random.value);
            }

            return pool.FirstOrDefault();
        }

        private int ResolveMerchantCostForChoice(RunEventDef runEvent, bool riskyChoice)
        {
            int baseCost = ResolveMerchantBaseCost(runEvent);
            int visit = Mathf.Max(1, _run != null ? _run.MerchantVisitCount : 1);
            int earlyMin = _content != null ? Mathf.Max(0, _content.EarlyShopMinPrice) : 5;
            int earlyMax = _content != null ? Mathf.Max(earlyMin, _content.EarlyShopMaxPrice) : 12;
            if (visit <= 2)
            {
                baseCost = Mathf.Clamp(baseCost, earlyMin, earlyMax);
            }

            int finalCost = Mathf.RoundToInt(baseCost * (riskyChoice ? 1.35f : 1f));
            if (!riskyChoice && visit <= 2 && _run != null && finalCost > _run.Gold)
            {
                finalCost = Mathf.Max(0, _run.Gold);
            }

            return Mathf.Max(0, finalCost);
        }

        private int ResolveMerchantBaseCost(RunEventDef runEvent)
        {
            if (_content != null && _content.Shops != null && _content.Shops.Count > 0)
            {
                int visit = Mathf.Max(1, _run != null ? _run.MerchantVisitCount : 1);
                int index = Mathf.Clamp(visit - 1, 0, _content.Shops.Count - 1);
                ShopProfileDef profile = _content.Shops[index];
                if (profile != null)
                {
                    float growth = Mathf.Clamp(profile.PriceGrowth, 1f, 1.6f);
                    float scaled = profile.BasePrice * Mathf.Pow(growth, Mathf.Clamp(index, 0, 8) * 0.42f);
                    return Mathf.Max(1, Mathf.RoundToInt(scaled));
                }
            }

            return runEvent != null ? Mathf.Max(0, runEvent.GoldCost) : 0;
        }

        private UpgradeDef PickEarlyAutomationOrExpansionGift()
        {
            if (_content == null || _content.Upgrades == null || _run == null)
            {
                return null;
            }

            bool canExpand = _run.ActiveHoleCount < _run.MaxHoleCount;
            float unlockWindow = _run.ElapsedSeconds + 130f;
            IEnumerable<UpgradeDef> basePool = _content.Upgrades
                .Where(u => u != null &&
                            (!_run.UpgradeStacks.ContainsKey(u.Id) || _run.UpgradeStacks[u.Id] < u.MaxStacks) &&
                            u.UnlockAtSecond <= unlockWindow);

            IEnumerable<UpgradeDef> focusPool = basePool.Where(u =>
                u.EffectType == UpgradeEffectType.AddActiveHole ||
                u.EffectType == UpgradeEffectType.UnlockAutoHammer ||
                u.EffectType == UpgradeEffectType.AutoHammerIntervalMultiplier ||
                u.EffectType == UpgradeEffectType.DeployAutoHammerTower ||
                u.EffectType == UpgradeEffectType.DeploySensorHammer ||
                u.EffectType == UpgradeEffectType.DeployGoldMagnet ||
                u.EffectType == UpgradeEffectType.AddDroneCount ||
                u.Tags.Contains("Automation") ||
                u.Tags.Contains("Facility") ||
                u.Tags.Contains("Expansion"));

            if (!focusPool.Any())
            {
                return null;
            }

            return focusPool
                .OrderByDescending(def => ScoreEarlyGiftUpgrade(def, canExpand))
                .ThenBy(def => def.UnlockAtSecond)
                .ThenBy(_ => UnityEngine.Random.value)
                .FirstOrDefault();
        }

        private float ScoreEarlyGiftUpgrade(UpgradeDef def, bool canExpand)
        {
            if (def == null)
            {
                return -999f;
            }

            float score = def.BaseWeight + (int)def.Rarity * 0.35f;
            if (def.EffectType == UpgradeEffectType.AddActiveHole)
            {
                score += canExpand ? 6f : -4f;
            }

            if (def.EffectType == UpgradeEffectType.UnlockAutoHammer)
            {
                score += _run.Stats.AutoHammerInterval > 0f ? 0.6f : 5f;
            }

            if (def.EffectType == UpgradeEffectType.AutoHammerIntervalMultiplier)
            {
                score += _run.Stats.AutoHammerInterval > 0f ? 4.5f : 1.4f;
            }

            if (def.EffectType == UpgradeEffectType.DeployAutoHammerTower ||
                def.EffectType == UpgradeEffectType.DeploySensorHammer ||
                def.EffectType == UpgradeEffectType.DeployGoldMagnet)
            {
                score += _run.ActiveFacilityCount > 0 ? 1.2f : 4.2f;
            }

            if (def.EffectType == UpgradeEffectType.AddDroneCount)
            {
                score += _run.Stats.DroneCount > 0 ? 1.6f : 2.8f;
            }

            if (def.Tags.Contains("Expansion") || def.Tags.Contains("Automation"))
            {
                score += 1.2f;
            }

            return score;
        }

        private static float ResolveEventRewardScale(RunEventDef runEvent, int choiceIndex)
        {
            float baseScale = choiceIndex switch
            {
                0 => 1f,
                1 => 1.35f,
                _ => 0f,
            };
            return baseScale * Mathf.Clamp(runEvent != null ? runEvent.RewardMult : 1f, 0.55f, 2f);
        }

        private static float ResolveEventRiskScale(RunEventDef runEvent, int choiceIndex)
        {
            float baseScale = choiceIndex switch
            {
                0 => 0.55f,
                1 => 1f,
                _ => 0f,
            };
            return baseScale * Mathf.Clamp(runEvent != null ? runEvent.RiskMult : 0.1f, 0f, 2f);
        }

        private static bool IsSkipChoice(RunEventDef runEvent, int choiceIndex)
        {
            if (runEvent?.Choices == null || choiceIndex < 0 || choiceIndex >= runEvent.Choices.Count)
            {
                return choiceIndex >= 2;
            }

            string token = runEvent.Choices[choiceIndex] ?? string.Empty;
            return token.IndexOf("Skip", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private RunEventDef SelectEventForCurrentStage(string excludeId)
        {
            List<RunEventDef> candidates = _content.Events
                .Where(e => _run.ElapsedSeconds >= e.MinTime &&
                            _run.ElapsedSeconds <= e.MaxTime &&
                            (!HasChallenge("CHAL_01") || e.Type != RunEventType.MerchantBoost) &&
                            (string.IsNullOrEmpty(excludeId) || e.Id != excludeId))
                .ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            float total = 0f;
            List<float> weights = new List<float>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                RunEventDef def = candidates[i];
                float weight = ComputeEventWeight(def);
                weights.Add(weight);
                total += weight;
            }

            if (total <= 0f)
            {
                return candidates[UnityEngine.Random.Range(0, candidates.Count)];
            }

            float roll = UnityEngine.Random.value * total;
            float acc = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                acc += weights[i];
                if (roll <= acc)
                {
                    return candidates[i];
                }
            }

            return candidates[candidates.Count - 1];
        }

        private float ComputeEventWeight(RunEventDef def)
        {
            float t = _run.ElapsedSeconds;
            float runDuration = GetRunDurationSeconds();
            float earlyThreshold = runDuration * (220f / 600f);
            float midThreshold = runDuration * (420f / 600f);
            float weight = def.Type switch
            {
                RunEventType.MerchantBoost => t < earlyThreshold ? 1.35f : (t < midThreshold ? 0.85f : 0.58f),
                RunEventType.TreasureRush => t < earlyThreshold ? 1.3f : (t < midThreshold ? 1.05f : 0.9f),
                RunEventType.CurseAltar => t < earlyThreshold ? 0.6f : (t < midThreshold ? 0.95f : 1.28f),
                RunEventType.RepairStation => t < earlyThreshold ? 1.12f : (t < midThreshold ? 1f : 0.72f),
                RunEventType.BountyContract => t < earlyThreshold ? 0.35f : (t < midThreshold ? 1.38f : 1.22f),
                RunEventType.RogueHoleZone => t < earlyThreshold ? 0.24f : (t < midThreshold ? 1.3f : 1.42f),
                _ => 1f,
            };

            if (HasChallenge("CHAL_01") && def.Type == RunEventType.MerchantBoost)
            {
                return 0.01f;
            }

            if (HasChallenge("CHAL_02") && def.Type == RunEventType.RepairStation)
            {
                weight *= 0.18f;
            }

            if (def.Type == RunEventType.TreasureRush && _run.TreasureRushRemaining > 3f)
            {
                weight *= 0.35f;
            }

            if (def.Type == RunEventType.CurseAltar && _run.CurseRemaining > 3f)
            {
                weight *= 0.35f;
            }

            if (def.Type == RunEventType.BountyContract && _run.BountyContractRemaining > 3f)
            {
                weight *= 0.35f;
            }

            if (_run.Durability <= Mathf.CeilToInt(_run.MaxDurability * 0.5f) && def.Type == RunEventType.RepairStation)
            {
                weight *= 1.95f;
            }

            if (_run.ActiveFacilityCount > 0 &&
                (def.Type == RunEventType.BountyContract || def.Type == RunEventType.RogueHoleZone))
            {
                weight *= 1.2f;
            }

            return Mathf.Max(0.01f, weight);
        }

        private void ActivateRogueZone(float duration, int holeCount)
        {
            ClearRogueHolePressure();
            List<HoleRuntime> selected = _holes
                .Where(h => h != null && h.IsActive)
                .OrderByDescending(h => h.DangerLevel * 2f + h.SpawnWeight)
                .ThenBy(h => h.Index)
                .Take(Mathf.Clamp(holeCount, 1, Mathf.Max(1, _holes.Count(h => h != null && h.IsActive))))
                .ToList();
            for (int i = 0; i < selected.Count; i++)
            {
                ApplyRogueHolePressure(selected[i], duration);
            }

            _run.RogueZoneBurstCount++;
            _run.RogueZoneRemaining = Mathf.Max(_run.RogueZoneRemaining, duration);
        }

        private void TickBoss(float deltaTime)
        {
            if (!HasActiveBoss())
            {
                return;
            }

            _bossEncounterService.TickActiveEncounter(
                _run,
                _activeBossEncounter,
                deltaTime,
                _holes,
                (hole, duration) => ApplyRogueHolePressure(hole, duration),
                shieldActive =>
                {
                    if (_boss != null)
                    {
                        _boss.SetShieldActive(shieldActive);
                        if (shieldActive)
                        {
                            ShowMessage("Boss 护盾展开", 0.9f, 2);
                        }
                    }
                });
            _bossSpawnScale = _bossEncounterService.ResolveSpawnScale(_activeBossEncounter);

            bool attacked = _boss.Tick(deltaTime);
            if (!attacked)
            {
                return;
            }

            int durabilityDamage = _boss.Def.DurabilityDamage;
            if (HasChallenge("CHAL_06"))
            {
                durabilityDamage += 1;
            }

            _run.Durability = Mathf.Max(0, _run.Durability - durabilityDamage);

            if (EnableAutoPilotForTests &&
                _activeBossEncounter != null &&
                _activeBossEncounter.Def != null &&
                !_activeBossEncounter.Def.IsFinalBoss)
            {
                int testFloor = Mathf.Max(2, Mathf.CeilToInt(_run.MaxDurability * 0.45f));
                _run.Durability = Mathf.Max(_run.Durability, testFloor);
            }

            TriggerCameraShake(1.8f);
            ShowMessage("Boss 重击命中农场", 0.8f, 2);
            if (_run.Durability <= 0)
            {
                EndRun(false, "Boss 摧毁了农场防线。", 10);
            }
        }

        private void TrySpawnBossEncounterIfNeeded()
        {
            if (_activeBossEncounter != null &&
                !IsFinalBossEncounter(_activeBossEncounter) &&
                _run.ElapsedSeconds >= GetRunDurationSeconds() &&
                !_run.BossSpawned)
            {
                if (_boss != null)
                {
                    _boss.Deactivate();
                }

                _activeBossEncounter.Defeated = true;
                _activeBossEncounter = null;
                _boss = null;
                _bossSpawnScale = 1f;
                ClearRogueHolePressure();
                ShowMessage("中期Boss退场，终局验收开始", 1.4f, 3);
            }

            if (HasActiveBoss())
            {
                return;
            }

            BossEncounterRuntime pending = _bossEncounterService.FindEncounterToSpawn(_bossTimeline, _run.ElapsedSeconds);
            if (pending == null || pending.Def == null || pending.Boss == null)
            {
                return;
            }

            if (!_bossLookup.TryGetValue(pending.Boss.Id, out BossRuntime runtime))
            {
                return;
            }

            pending.Spawned = true;
            _activeBossEncounter = pending;
            _boss = runtime;
            float hpScale = Mathf.Max(0.55f, pending.Def.HpMultiplier) *
                (1f + Mathf.Clamp01(_run.ElapsedSeconds / GetRunDurationSeconds()) * 0.18f);
            if (HasChallenge("CHAL_06"))
            {
                hpScale *= 1.16f;
            }

            _boss.Activate(hpScale);
            _boss.SetShieldActive(false);
            _bossSpawnScale = _bossEncounterService.ResolveSpawnScale(_activeBossEncounter);

            if (pending.Def.IsFinalBoss)
            {
                _run.BossSpawned = true;
                _combatCueService?.OnHit(false, false, true);
                ShowMessage($"10:00 - {pending.Boss.DisplayName} 出现", 2f, 3);
            }
            else
            {
                int emergencyRepair = Mathf.Max(2, Mathf.CeilToInt(_run.MaxDurability * 0.45f));
                if (_run.Durability < emergencyRepair)
                {
                    _run.Durability = emergencyRepair;
                    ShowMessage("中期补给到位：防线耐久应急修复", 1.2f, 2);
                }

                _run.MidBossSpawned = true;
                _combatCueService?.OnHit(false, false, true);
                ShowMessage($"5:00 - {pending.Boss.DisplayName} 出现", 1.8f, 3);
                if (_presentationSkin != null)
                {
                    PlayClip(_presentationSkin.MidBossSpawnSfx, 1f, 0.02f);
                }
            }
        }

        private void ApplyRogueHolePressure(HoleRuntime hole, float duration)
        {
            if (hole == null)
            {
                return;
            }

            _rogueHoleIndices.Add(hole.Index);
            hole.SetEventPressure(true);
            _run.RogueZoneRemaining = Mathf.Max(_run.RogueZoneRemaining, duration);
        }

        private void ClearRogueHolePressure()
        {
            for (int i = 0; i < _holes.Count; i++)
            {
                _holes[i].SetEventPressure(false);
            }

            _rogueHoleIndices.Clear();
        }

        private void CheckMilestones()
        {
            float runDuration = GetRunDurationSeconds();
            float facilitySecond = runDuration * 0.4f;
            float buildSecond = runDuration * 0.5f;
            float overdriveSecond = runDuration * 0.7f;
            float automationGuaranteeTarget = _starterAutomationTargetSecond > 0f
                ? _starterAutomationTargetSecond
                : (_content != null ? Mathf.Clamp(_content.AutomationGuaranteeMaxSeconds, 20f, 240f) : 45f);

            if (!HasRealAutomation() &&
                _run.StarterAutomationGrantedSecond < 0f &&
                _run.ElapsedSeconds >= automationGuaranteeTarget)
            {
                GrantStarterAutomationPackage();
            }

            if (HasRealAutomation() && !_run.AutomationMilestoneReached)
            {
                _run.AutomationMilestoneReached = true;
                if (_run.FirstAutomationSecond < 0f)
                {
                    _run.FirstAutomationSecond = _run.ElapsedSeconds;
                }
                if (_run.FirstReliefSecond < 0f)
                {
                    _run.FirstReliefSecond = _run.ElapsedSeconds;
                }

                ShowMessage($"自动化上线：已进入半自动期（形态 {_run.CurrentAutomationForms}）", 1.2f, 2);
            }

            int earlyHoleTarget = 0;
            if (_run.ElapsedSeconds >= 120f)
            {
                earlyHoleTarget = Mathf.Min(_run.MaxHoleCount, 8);
            }
            else if (_run.ElapsedSeconds >= 90f)
            {
                earlyHoleTarget = Mathf.Min(_run.MaxHoleCount, 7);
            }
            else if (_run.ElapsedSeconds >= 40f)
            {
                earlyHoleTarget = Mathf.Min(_run.MaxHoleCount, 6);
            }

            if (earlyHoleTarget > 0 && _run.ActiveHoleCount < earlyHoleTarget)
            {
                int unlocked = UnlockNextActiveHoles(earlyHoleTarget - _run.ActiveHoleCount, false);
                if (unlocked > 0)
                {
                    ShowMessage($"生产线扩建：已激活 {_run.ActiveHoleCount}/{_run.MaxHoleCount} 洞口", 1.05f, 2);
                }
            }

            if (!_run.BuildMilestoneReached && _run.ElapsedSeconds >= buildSecond)
            {
                if (_run.BuildTags.Count < 3)
                {
                    _run.BuildTags.Add("Damage");
                    _run.BuildTags.Add("Range");
                    _run.BuildTags.Add("Automation");
                }

                if (!_run.Stats.AutoAim && _run.Stats.DroneCount <= 0)
                {
                    _run.Stats.AutoAim = true;
                }

                if (_run.Stats.AutoHammerInterval <= 0f)
                {
                    _run.Stats.AutoHammerInterval = 1.18f;
                }
                else if (_run.Stats.AutoHammerInterval > 1.18f)
                {
                    _run.Stats.AutoHammerInterval = Mathf.Max(1.1f, _run.Stats.AutoHammerInterval * 0.84f);
                }

                _run.BuildMilestoneReached = true;
                if (_run.FirstBuildFormedSecond < 0f)
                {
                    _run.FirstBuildFormedSecond = _run.ElapsedSeconds;
                }
                ShowMessage("流派成型：构筑方向已明确。", 1.4f);
            }

            if (!_run.FacilityMilestoneReached && _run.ElapsedSeconds >= facilitySecond)
            {
                if (_run.ActiveFacilityCount <= 0)
                {
                    ApplyFacilityDeployUpgrade(FacilityType.AutoHammerTower, 1);
                }

                _run.FacilityMilestoneReached = true;
                ShowMessage("设施开始介入清理。", 1.25f);
            }

            if (!_run.FacilityOverdriveReached && _run.ElapsedSeconds >= overdriveSecond)
            {
                _run.FacilityOverdriveReached = true;
                _run.FacilityOverloadTimer = Mathf.Max(_run.FacilityOverloadTimer, 7f);
                _run.FacilityCooldownMultiplier = Mathf.Clamp(_run.FacilityCooldownMultiplier * 0.88f, 0.45f, 1.2f);

                _run.Stats.AutoAim = true;
                if (_run.Stats.AutoHammerInterval <= 0f)
                {
                    _run.Stats.AutoHammerInterval = 1.08f;
                }
                else
                {
                    _run.Stats.AutoHammerInterval = Mathf.Min(_run.Stats.AutoHammerInterval, 1.08f);
                }

                if (_run.Stats.DroneCount <= 0)
                {
                    _run.Stats.DroneCount = 1;
                }

                if (_run.ActiveFacilityCount < 3)
                {
                    ApplyFacilityDeployUpgrade(FacilityType.SensorHammer, 1);
                }

                if (_run.ActiveFacilityCount < 3)
                {
                    ApplyFacilityDeployUpgrade(FacilityType.GoldMagnet, 1);
                }

                if (_run.ActiveFacilityCount < 4)
                {
                    ApplyFacilityDeployUpgrade(FacilityType.TeslaCoupler, 1);
                }

                if (_run.ActiveFacilityCount < 4)
                {
                    ApplyFacilityDeployUpgrade(FacilityType.ExecutionPlate, 1);
                }

                ShowMessage("工厂联动高频阶段开启。", 1.4f);
            }
        }

        private void GrantStarterAutomationPackage()
        {
            if (_run == null || _run.Stats == null || _run.StarterAutomationGrantedSecond >= 0f)
            {
                return;
            }

            bool changed = false;
            if (_run.Stats.AutoHammerInterval <= 0f)
            {
                _run.Stats.AutoHammerInterval = 1.58f;
                changed = true;
            }

            FacilityType grantedFacilityType = FacilityType.AutoHammerTower;
            if (_run.ActiveFacilityCount <= 0)
            {
                grantedFacilityType = RollStarterFacilityType();
                ApplyFacilityDeployUpgrade(grantedFacilityType, 1);
                changed = true;
            }

            int holeTarget = Mathf.Min(_run.MaxHoleCount, 6);
            if (_run.ActiveHoleCount < holeTarget)
            {
                int unlocked = UnlockNextActiveHoles(holeTarget - _run.ActiveHoleCount, false);
                if (unlocked > 0)
                {
                    changed = true;
                }
            }

            RefreshAutomationProgress();
            if (changed)
            {
                _run.StarterAutomationGrantedSecond = _run.ElapsedSeconds;
                _run.StarterAutomationPackageGiven = true;
                string facilityName = ShortFacilityName(grantedFacilityType);
                ShowMessage($"40s 保底自动化：自动锤 + {facilityName}", 1.2f, 2);
            }
        }

        private void EnsureFirstUpgradePacing()
        {
            if (_run == null || _run.RunEnded || _run.FirstUpgradeSecond >= 0f)
            {
                return;
            }

            float earliest = GetFirstUpgradeEarliestSecond();
            float latest = GetFirstUpgradeLatestSecond();
            if (_run.ElapsedSeconds < earliest)
            {
                return;
            }

            if (_run.PendingLevelUps > 0)
            {
                return;
            }

            if (_run.ElapsedSeconds < latest)
            {
                return;
            }

            _run.PendingLevelUps = 1;
            ShowMessage("工坊发放应急升级方案。", 1.1f, 2);
        }

        private void UpdatePacingSnapshot()
        {
            if (_run == null || _run.Stats == null)
            {
                return;
            }

            if (_run.FirstAutomationSecond < 0f && _run.CurrentAutomationForms > 0)
            {
                _run.FirstAutomationSecond = _run.ElapsedSeconds;
            }

            if (_run.FirstReliefSecond < 0f &&
                (_run.AutomationMilestoneReached || HasRealAutomation()))
            {
                _run.FirstReliefSecond = _run.ElapsedSeconds;
            }

            if (_run.FirstBuildFormedSecond < 0f &&
                !string.Equals(_run.BuildIdentity, "未成型", StringComparison.Ordinal))
            {
                _run.FirstBuildFormedSecond = _run.ElapsedSeconds;
            }
        }

        private void TickWaveModDirector(float deltaTime)
        {
            if (_run == null)
            {
                return;
            }

            if (_run.WaveModRemaining > 0f)
            {
                _run.WaveModRemaining = Mathf.Max(0f, _run.WaveModRemaining - deltaTime);
                if (_run.WaveModRemaining <= 0f)
                {
                    _activeWaveMod = null;
                    _run.WaveModId = string.Empty;
                    _run.WaveModName = string.Empty;
                    _run.WaveThreatBonus = 0f;
                    _run.WaveSpeedBonus = 0f;
                    _run.WaveRareBonus = HasChallenge("CHAL_04") ? 0.18f : 0f;
                    _run.WaveEliteBonus = 0f;
                    ShowMessage("波次修饰结束", 0.7f, 1);
                }

                return;
            }

            if (_content == null || _content.WaveMods == null || _content.WaveMods.Count == 0)
            {
                return;
            }

            if (_run.ElapsedSeconds < Mathf.Max(20f, _run.NextWaveModRollSecond))
            {
                return;
            }

            List<WaveModDef> candidates = _content.WaveMods.Where(mod => mod != null).ToList();
            if (candidates.Count == 0)
            {
                return;
            }

            _activeWaveMod = candidates[_random.Next(0, candidates.Count)];
            _run.WaveModId = _activeWaveMod.Id;
            _run.WaveModName = _activeWaveMod.Name;
            _run.WaveThreatBonus = Mathf.Clamp(_activeWaveMod.ThreatAdd, -0.65f, 1.8f);
            _run.WaveSpeedBonus = Mathf.Clamp(_activeWaveMod.SpeedAdd, -0.65f, 1.8f);
            _run.WaveRareBonus = Mathf.Clamp(
                _activeWaveMod.RareAdd + (HasChallenge("CHAL_04") ? 0.18f : 0f),
                -0.65f,
                1.8f);
            _run.WaveEliteBonus = Mathf.Clamp(_activeWaveMod.EliteAdd, -0.65f, 1.8f);
            _run.WaveModRemaining = Mathf.Clamp(20f + (int)_activeWaveMod.Rarity * 4.5f, 18f, 42f);
            _run.NextWaveModRollSecond = _run.ElapsedSeconds + UnityEngine.Random.Range(65f, 90f);

            string name = string.IsNullOrWhiteSpace(_run.WaveModName) ? "未知修饰" : _run.WaveModName;
            ShowMessage($"波次修饰生效：{name}", 1.1f, 2);
        }

        private void EndRun(bool win, string reason, int bonusChips)
        {
            if (_run.RunEnded)
            {
                return;
            }

            _run.RunEnded = true;
            _run.RunWon = win;

            _meta.TotalRuns++;
            if (win)
            {
                _meta.TotalWins++;
                _meta.LegendaryGears += 1;
            }

            int duration = Mathf.RoundToInt(_run.ElapsedSeconds);
            int workshopGain = Mathf.RoundToInt(_run.CoreShards + _run.Gold * 0.03f) + bonusChips;
            _meta.WorkshopChips += workshopGain;
            _meta.LifetimeGold += _run.Gold;
            _meta.LifetimeKills += _run.TotalKills;
            _run.BuildIdentity = ResolveBuildIdentity();
            RecordRecentRunSummary(duration, workshopGain);

            foreach (string codex in _run.CodexUnlockedThisRun)
            {
                if (!_meta.CodexEntries.Contains(codex))
                {
                    _meta.CodexEntries.Add(codex);
                }
            }

            List<AchievementDef> newlyUnlocked = _achievementService.Evaluate(_content, _run, _meta);
            _saveRepository.Save(_meta);

            _endOpen = true;
            _endPanel.SetActive(true);

            if (_endPanelImage != null)
            {
                Sprite resultBg = null;
                if (_externalUiSkin != null)
                {
                    resultBg = win
                        ? _externalUiSkin.ResultVictoryBackgroundSprite
                        : _externalUiSkin.ResultDefeatBackgroundSprite;
                }

                if (resultBg != null)
                {
                    _endPanelImage.sprite = resultBg;
                    _endPanelImage.type = Image.Type.Simple;
                    _endPanelImage.color = new Color(1f, 1f, 1f, 0.94f);
                }
                else
                {
                    _endPanelImage.sprite = null;
                    _endPanelImage.type = Image.Type.Simple;
                    _endPanelImage.color = new Color(0f, 0f, 0f, 0.82f);
                }
            }

            if (_endResultStamp != null)
            {
                Sprite stamp = null;
                if (_externalUiSkin != null)
                {
                    stamp = win
                        ? _externalUiSkin.ResultStampVictorySprite
                        : _externalUiSkin.ResultStampDefeatSprite;
                }

                if (stamp != null)
                {
                    _endResultStamp.sprite = stamp;
                    _endResultStamp.color = new Color(1f, 1f, 1f, 0.94f);
                    _endResultStamp.gameObject.SetActive(true);
                }
                else
                {
                    _endResultStamp.gameObject.SetActive(false);
                }
            }

            string achievementText = newlyUnlocked.Count == 0
                ? "无新成就"
                : string.Join("、", newlyUnlocked.Select(a => a.DisplayName));
            int totalCollectedGold = _run.ManualGoldCollected + _run.AutomationGoldCollected;
            float automationRatio = totalCollectedGold > 0
                ? (float)_run.AutomationGoldCollected / totalCollectedGold
                : 0f;
            string midBossStatus = _run.MidBossDefeated ? "已击败" : (_run.MidBossSpawned ? "未击败" : "未触发");
            string finalBossStatus = _run.BossDefeated ? "已击败" : (_run.BossSpawned ? "未击败" : "未触发");
            string earlyTtkSummary = _run.EarlyCommonKillSamples > 0
                ? $"{_run.EarlyCommonManualHitAverage:0.00} 锤 (样本 {_run.EarlyCommonKillSamples})"
                : "样本不足";
            float firstAutomation = _run.FirstAutomationSecond >= 0f ? _run.FirstAutomationSecond : _run.ElapsedSeconds;

            _endSummary.text =
                (win ? "胜利" : "失败") + "\n" +
                reason + "\n\n" +
                $"时长: {duration}s\n" +
                $"难度: {(_activeDifficulty != null ? _activeDifficulty.Name : _run.DifficultyId)}  挑战: {(_activeChallenge != null ? _activeChallenge.Name : (string.IsNullOrWhiteSpace(_run.ChallengeId) ? "无" : _run.ChallengeId))}\n" +
                $"金币: {_run.Gold}\n" +
                $"核心: {_run.CoreShards}\n" +
                $"击杀: {_run.TotalKills}\n" +
                $"稀有击杀: {_run.RareKillCount}\n" +
                $"最高连击: {_run.HighestCombo}\n" +
                $"事件参与: {_run.EventParticipationCount}  剩余券: {_run.EventTickets}\n" +
                $"中期Boss: {midBossStatus}  终局Boss: {finalBossStatus}\n" +
                $"自动化贡献: {automationRatio * 100f:0}% ({_run.AutomationGoldCollected}/{Mathf.Max(1, totalCollectedGold)})\n" +
                $"首次自动化: {firstAutomation:0.0}s  自动形态: {_run.CurrentAutomationForms}\n" +
                $"前期普通鼠锤数: {earlyTtkSummary}\n" +
                $"单次收益峰值: {_run.PeakSingleIncome}\n" +
                $"流派标签: {_run.BuildIdentity}\n" +
                $"工坊芯片 +{workshopGain}\n" +
                $"新成就: {achievementText}\n\n" +
                "按下“工坊成长”可购买永久节点";

            float firstUpgrade = _run.FirstUpgradeSecond >= 0f ? _run.FirstUpgradeSecond : _run.ElapsedSeconds;
            float firstRelief = _run.FirstReliefSecond >= 0f ? _run.FirstReliefSecond : _run.ElapsedSeconds;
            float firstBuild = _run.FirstBuildFormedSecond >= 0f ? _run.FirstBuildFormedSecond : _run.ElapsedSeconds;
            Debug.Log(
                $"[MoleSurvivors][PacingSnapshot] first_upgrade={firstUpgrade:0.0}s, " +
                $"first_auto={firstAutomation:0.0}s, first_relief={firstRelief:0.0}s, first_build={firstBuild:0.0}s, " +
                $"early_common_hits_avg={_run.EarlyCommonManualHitAverage:0.00}, early_common_samples={_run.EarlyCommonKillSamples}, " +
                $"automation_ratio={automationRatio * 100f:0.#}%, upgrades={_run.UpgradeStacks.Values.Sum()}, " +
                $"run_end={_run.ElapsedSeconds:0.0}s, won={_run.RunWon}");
        }

        private void RecordRecentRunSummary(int durationSeconds, int workshopGain)
        {
            if (_meta == null || _run == null)
            {
                return;
            }

            if (_meta.RecentRuns == null)
            {
                _meta.RecentRuns = new List<RunSummary>();
            }

            int totalCollectedGold = _run.ManualGoldCollected + _run.AutomationGoldCollected;
            float automationRatio = totalCollectedGold > 0
                ? (float)_run.AutomationGoldCollected / totalCollectedGold
                : 0f;
            RunSummary summary = new RunSummary
            {
                Won = _run.RunWon,
                Gold = _run.Gold,
                CoreShards = _run.CoreShards,
                Kills = _run.TotalKills,
                RareKills = _run.RareKillCount,
                EventParticipations = _run.EventParticipationCount,
                HighestCombo = _run.HighestCombo,
                MidBossDefeated = _run.MidBossDefeated,
                FinalBossDefeated = _run.BossDefeated,
                AutomationContribution = automationRatio,
                PeakIncome = _run.PeakSingleIncome,
                DurationSeconds = Mathf.Max(0, durationSeconds),
                WorkshopGain = workshopGain,
                DifficultyId = _run.DifficultyId,
                ChallengeId = _run.ChallengeId,
                WaveModId = _run.WaveModId,
                BuildIdentity = _run.BuildIdentity,
                ReadabilityAlertCount = _run.ReadabilityAlertCount,
            };

            _meta.RecentRuns.Insert(0, summary);
            if (_meta.RecentRuns.Count > MaxRecentRunHistory)
            {
                _meta.RecentRuns.RemoveRange(MaxRecentRunHistory, _meta.RecentRuns.Count - MaxRecentRunHistory);
            }
        }

        private void SetMetaPanelVisible(bool visible)
        {
            _metaOpen = visible;
            _metaPanel.SetActive(visible);
            if (!visible)
            {
                return;
            }

            RebuildMetaPanel();
        }

        private void RebuildMetaPanel()
        {
            _metaHeader.text =
                $"工坊芯片: {_meta.WorkshopChips}  |  传奇齿轮: {_meta.LegendaryGears}\n" +
                $"图鉴: {_meta.CodexEntries.Count}/{_content.CodexEntries.Count}  |  成就: {_meta.AchievementIds.Count}/{_content.Achievements.Count}\n" +
                $"当前武器: {GetWeaponName(_meta.ActiveWeaponId)}  |  当前角色: {GetCharacterName(_meta.ActiveCharacterId)}";

            for (int i = _metaListRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(_metaListRoot.GetChild(i).gameObject);
            }

            CreateMetaSectionLabel("武器选择");
            foreach (WeaponDef weapon in _content.Weapons.OrderBy(w => w.DisplayName))
            {
                AddWeaponSelectRow(weapon.Id, weapon.DisplayName);
            }

            CreateMetaSectionLabel("角色选择");
            foreach (CharacterDef character in _content.Characters.OrderBy(c => c.DisplayName))
            {
                AddCharacterSelectRow(character.Id, character.DisplayName);
            }

            CreateMetaSectionLabel("永久成长树");
            foreach (MetaNodeDef node in _content.MetaNodes.OrderBy(n => n.Id))
            {
                AddMetaNodeRow(node);
            }

            CreateMetaSectionLabel("成就列表");
            foreach (AchievementDef ach in _content.Achievements)
            {
                string status = _meta.AchievementIds.Contains(ach.Id) ? "[已完成]" : "[未完成]";
                CreateMetaInfoRow($"{status} {ach.DisplayName} - {ach.Description}");
            }
        }

        private void AddWeaponSelectRow(string weaponId, string name)
        {
            bool unlocked = _meta.UnlockedWeapons.Contains(weaponId);
            bool active = _meta.ActiveWeaponId == weaponId;
            string label = unlocked
                ? (active ? $"使用中: {name}" : $"切换到 {name}")
                : $"未解锁: {name}";

            Button button = CreateMetaActionRow(label, unlocked && !active);
            if (!unlocked || active)
            {
                return;
            }

            button.onClick.AddListener(() =>
            {
                _meta.ActiveWeaponId = weaponId;
                _saveRepository.Save(_meta);
                RebuildMetaPanel();
            });
        }

        private void AddCharacterSelectRow(string characterId, string name)
        {
            bool unlocked = _meta.UnlockedCharacters.Contains(characterId);
            bool active = _meta.ActiveCharacterId == characterId;
            string label = unlocked
                ? (active ? $"使用中: {name}" : $"切换到 {name}")
                : $"未解锁: {name}";

            Button button = CreateMetaActionRow(label, unlocked && !active);
            if (!unlocked || active)
            {
                return;
            }

            button.onClick.AddListener(() =>
            {
                _meta.ActiveCharacterId = characterId;
                _saveRepository.Save(_meta);
                RebuildMetaPanel();
            });
        }

        private void AddMetaNodeRow(MetaNodeDef node)
        {
            int level = MetaStateUtils.GetNodeLevel(_meta, node.Id);
            int cost = node.Cost * (level + 1);
            bool canPurchase = CanPurchaseNode(node, level, cost);
            string progress = node.MaxLevel <= 1 ? (level > 0 ? "已解锁" : "未解锁") : $"Lv {level}/{node.MaxLevel}";
            string label = $"{node.DisplayName} ({progress}) - 费用 {cost} - {node.Description}";

            Button button = CreateMetaActionRow(label, canPurchase);
            if (!canPurchase)
            {
                return;
            }

            button.onClick.AddListener(() =>
            {
                PurchaseNode(node, level, cost);
                RebuildMetaPanel();
            });
        }

        private bool CanPurchaseNode(MetaNodeDef node, int level, int cost)
        {
            if (level >= node.MaxLevel)
            {
                return false;
            }

            if (_meta.WorkshopChips < cost)
            {
                return false;
            }

            for (int i = 0; i < node.Requires.Count; i++)
            {
                if (MetaStateUtils.GetNodeLevel(_meta, node.Requires[i]) <= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private void PurchaseNode(MetaNodeDef node, int level, int cost)
        {
            _meta.WorkshopChips -= cost;
            MetaStateUtils.SetNodeLevel(_meta, node.Id, level + 1);

            switch (node.EffectType)
            {
                case MetaEffectType.UnlockLightningWeapon:
                {
                    string weaponId = !string.IsNullOrWhiteSpace(node.TargetId)
                        ? node.TargetId
                        : _content.SecondaryWeaponUnlockId;
                    if (!string.IsNullOrWhiteSpace(weaponId))
                    {
                        UnlockWeapon(weaponId);
                    }

                    break;
                }
                case MetaEffectType.UnlockDroneWeapon:
                {
                    string weaponId = !string.IsNullOrWhiteSpace(node.TargetId)
                        ? node.TargetId
                        : _content.TertiaryWeaponUnlockId;
                    if (!string.IsNullOrWhiteSpace(weaponId))
                    {
                        UnlockWeapon(weaponId);
                    }

                    break;
                }
                case MetaEffectType.UnlockEngineerCharacter:
                {
                    string characterId = !string.IsNullOrWhiteSpace(node.TargetId)
                        ? node.TargetId
                        : _content.SecondaryCharacterUnlockId;
                    if (!string.IsNullOrWhiteSpace(characterId))
                    {
                        UnlockCharacter(characterId);
                    }

                    break;
                }
            }

            _saveRepository.Save(_meta);
        }

        private void UnlockWeapon(string weaponId)
        {
            if (!_meta.UnlockedWeapons.Contains(weaponId))
            {
                _meta.UnlockedWeapons.Add(weaponId);
            }
        }

        private void UnlockCharacter(string characterId)
        {
            if (!_meta.UnlockedCharacters.Contains(characterId))
            {
                _meta.UnlockedCharacters.Add(characterId);
            }
        }

        private void CreateMetaSectionLabel(string text)
        {
            GameObject row = new GameObject("Section", typeof(RectTransform), typeof(Text));
            row.transform.SetParent(_metaListRoot, false);
            RectTransform rect = row.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 46f);
            Text label = row.GetComponent<Text>();
            label.font = GetBuiltinUiFont();
            label.fontSize = 28;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = new Color(1f, 0.9f, 0.55f);
            label.text = text;
        }

        private void CreateMetaInfoRow(string text)
        {
            GameObject row = new GameObject("InfoRow", typeof(RectTransform), typeof(Text));
            row.transform.SetParent(_metaListRoot, false);
            RectTransform rect = row.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 56f);
            Text label = row.GetComponent<Text>();
            label.font = GetBuiltinUiFont();
            label.fontSize = 22;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = new Color(0.94f, 0.94f, 0.94f);
            label.text = text;
        }

        private Button CreateMetaActionRow(string text, bool interactable)
        {
            GameObject row = new GameObject("ActionRow", typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(_metaListRoot, false);
            RectTransform rect = row.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 70f);

            Image image = row.GetComponent<Image>();
            image.color = interactable ? new Color(0.16f, 0.27f, 0.34f, 0.95f) : new Color(0.15f, 0.15f, 0.15f, 0.88f);
            Button button = row.GetComponent<Button>();
            button.interactable = interactable;

            GameObject textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(row.transform, false);
            RectTransform textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(14f, 8f);
            textRect.offsetMax = new Vector2(-14f, -8f);
            Text label = textGo.GetComponent<Text>();
            label.font = GetBuiltinUiFont();
            label.fontSize = 21;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = interactable ? Color.white : new Color(0.72f, 0.72f, 0.72f);
            label.text = text;
            return button;
        }

        private string ResolveStageLabel(float runDuration)
        {
            if (_run == null)
            {
                return "准备中";
            }

            if (HasActiveBoss())
            {
                return _activeBossEncounter != null && _activeBossEncounter.Def != null && !_activeBossEncounter.Def.IsFinalBoss
                    ? "中期Boss战"
                    : "终局Boss战";
            }

            float elapsed = _run.ElapsedSeconds;
            if (!HasRealAutomation())
            {
                return "手动期";
            }

            float facilitySecond = runDuration * 0.4f;
            float buildSecond = runDuration * 0.5f;
            float overdriveSecond = runDuration * 0.7f;
            if (elapsed < facilitySecond)
            {
                return "半自动期";
            }

            if (elapsed < buildSecond)
            {
                return "设施展开期";
            }

            if (elapsed < overdriveSecond)
            {
                return "中期验收期";
            }

            return "工厂暴走期";
        }

        private void UpdateHud()
        {
            if (_run == null)
            {
                return;
            }

            int minutes = Mathf.FloorToInt(_run.ElapsedSeconds / 60f);
            int seconds = Mathf.FloorToInt(_run.ElapsedSeconds % 60f);
            float runDuration = GetRunDurationSeconds();
            string stage = ResolveStageLabel(runDuration);
            string diffLabel = _activeDifficulty != null ? _activeDifficulty.Name : (_run.DifficultyId ?? "标准");
            string challengeLabel = _activeChallenge != null ? _activeChallenge.Name : "无挑战";
            string waveLabel = string.IsNullOrWhiteSpace(_run.WaveModName)
                ? "波次: 常规"
                : $"波次: {_run.WaveModName} {_run.WaveModRemaining:0}s";

            _topHud.text = $"{minutes:00}:{seconds:00}  |  阶段: {stage}\n难度: {diffLabel}  挑战: {challengeLabel}  {waveLabel}";
            UpdateHudMeters();

            float overloadProgress = _run.FacilityOverloadThresholdCurrent > 0
                ? Mathf.Clamp01((float)_run.FacilityTriggerCount / _run.FacilityOverloadThresholdCurrent)
                : 0f;
            string overloadState = _run.FacilityOverloadTimer > 0f
                ? $"超载 {_run.FacilityOverloadTimer:0.0}s"
                : $"{overloadProgress * 100f:0}%";
            string facilityMix = BuildFacilityMixLabel();
            string facilityCd = BuildFacilityCooldownLabel();
            string buildFocus = BuildTopTagFocusLabel();
            string automationSources = BuildAutomationSourceLabel();
            string automationForms = BuildAutomationFormsLabel();
            string openingRouteLabel = BuildOpeningRouteLabel();
            string recentUpgrades = BuildRecentUpgradeHistoryLabel();
            string lastGain = string.IsNullOrWhiteSpace(_run.LastUpgradeDeltaSummary)
                ? "最近增益: 无"
                : $"最近增益: {_run.LastUpgradeDeltaSummary}";
            string earlyTtk = BuildEarlyCommonTtkLabel();

            _rightHud.text =
                $"金币: {_run.Gold}\n" +
                $"核心: {_run.CoreShards}\n" +
                $"等级: {_run.Level}\n" +
                $"经验: {Mathf.FloorToInt(_run.Experience)}/{Mathf.FloorToInt(_run.NextExperience)}\n" +
                $"耐久: {_run.Durability}/{_run.MaxDurability}\n" +
                $"连击: {_run.Combo}\n" +
                $"击杀: {_run.TotalKills}\n" +
                $"稀有击杀: {_run.RareKillCount}  事件券: {_run.EventTickets}\n" +
                $"洞口: {_run.ActiveHoleCount}/{Mathf.Max(1, _run.MaxHoleCount)}\n" +
                $"设施: {_run.ActiveFacilityCount}  进度: {overloadState}\n" +
                $"{facilityMix}\n" +
                $"{facilityCd}\n" +
                $"{openingRouteLabel}\n" +
                $"{automationForms}\n" +
                $"{automationSources}\n" +
                $"{buildFocus}";

            string autoStatus = _run.Stats.AutoHammerInterval > 0f
                ? $"自动锤 {Mathf.Max(0.1f, _run.Stats.AutoHammerInterval):0.00}s"
                : "自动锤 未解锁";
            string droneStatus = _run.Stats.DroneCount > 0 ? $"无人机 x{_run.Stats.DroneCount}" : "无人机 0";
            string evoStatus = _run.Evolutions.Count > 0 ? string.Join(",", _run.Evolutions) : "未进化";
            string rush = _run.TreasureRushRemaining > 0f ? "暴富x2" : "正常收益";
            string curse = _run.CurseRemaining > 0f ? "诅咒中" : "无诅咒";
            string bounty = _run.BountyContractRemaining > 0f ? "赏金合约" : "无赏金";
            string rogue = _run.RogueZoneRemaining > 0f ? "暴走洞区" : "洞区平稳";
            string bossTrack = $"中期Boss {(_run.MidBossDefeated ? "已过" : (_run.MidBossSpawned ? "进行中" : "未到"))} / 终局Boss {(_run.BossDefeated ? "已过" : (_run.BossSpawned ? "进行中" : "未到"))}";

            _bottomHud.text =
                $"武器: {GetWeaponName(_run.WeaponId)}  角色: {GetCharacterName(_run.CharacterId)}\n" +
                $"伤害 {_run.Stats.Damage:0.0}  攻速 {_run.Stats.AttackInterval:0.00}s  范围 {_run.Stats.AttackRadius:0.00}  暴击 {_run.Stats.CritChance * 100f:0}%\n" +
                $"{autoStatus}  |  {droneStatus}  |  磁吸 {_run.Stats.MagnetRadius:0.00}  |  进化 {evoStatus}\n" +
                $"状态: {rush} / {curse} / {bounty} / {rogue}  |  {bossTrack}  |  构筑: {ResolveBuildIdentity()}\n" +
                $"{earlyTtk}\n" +
                $"{lastGain}\n" +
                $"已选升级(最近6): {recentUpgrades}\n" +
                $"美术: {_activeArtSummary}  |  UI: {_activeUiSummary}  |  {_feedbackAssetSummary}";
        }

        private string BuildFacilityMixLabel()
        {
            int tower = _holes.Count(h => h.Facility != null && h.Facility.Type == FacilityType.AutoHammerTower);
            int sensor = _holes.Count(h => h.Facility != null && h.Facility.Type == FacilityType.SensorHammer);
            int magnet = _holes.Count(h => h.Facility != null && h.Facility.Type == FacilityType.GoldMagnet);
            int bounty = _holes.Count(h => h.Facility != null && h.Facility.Type == FacilityType.BountyMarker);
            int tesla = _holes.Count(h => h.Facility != null && h.Facility.Type == FacilityType.TeslaCoupler);
            int execute = _holes.Count(h => h.Facility != null && h.Facility.Type == FacilityType.ExecutionPlate);
            return $"锤塔{tower} 雷锤{sensor} 吸金{magnet} 赏金{bounty} 电网{tesla} 处决{execute}";
        }

        private string BuildFacilityCooldownLabel()
        {
            List<FacilityRuntime> facilities = _holes
                .Where(h => h.Facility != null)
                .Select(h => h.Facility)
                .ToList();
            if (facilities.Count == 0)
            {
                return "设施冷却: 无";
            }

            string cooldowns = string.Join(" | ", facilities
                .OrderBy(f => f.CooldownTimer)
                .ThenBy(f => f.Type)
                .Take(3)
                .Select(f => $"{ShortFacilityName(f.Type)} {Mathf.Max(0f, f.CooldownTimer):0.0}s"));
            return $"设施冷却: {cooldowns}";
        }

        private string BuildTopTagFocusLabel()
        {
            if (_run.TagLevels == null || _run.TagLevels.Count == 0)
            {
                return "构筑标签: 无";
            }

            List<KeyValuePair<string, int>> top = _run.TagLevels
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            if (top.Count == 0)
            {
                return "构筑标签: 无";
            }

            string text = string.Join(" | ", top.Select(pair => $"{pair.Key} Lv{pair.Value}"));
            return $"构筑标签: {text}";
        }

        private string BuildAutomationFormsLabel()
        {
            if (_run == null)
            {
                return "自动形态: 0";
            }

            string first = _run.FirstAutomationSecond >= 0f
                ? $"{_run.FirstAutomationSecond:0.0}s"
                : "--";
            return $"自动形态: {_run.CurrentAutomationForms}  首次: {first}";
        }

        private string BuildOpeningRouteLabel()
        {
            if (_run == null || string.IsNullOrWhiteSpace(_run.OpeningRoute))
            {
                return "开局路线: 默认";
            }

            string routeLabel = _run.OpeningRoute switch
            {
                OpeningRouteAutoTower => "自动锤阵",
                OpeningRouteChainGrid => "电链设施联动",
                OpeningRouteBountyFactory => "赏金工厂",
                _ => _run.OpeningRoute,
            };
            return $"开局路线: {routeLabel}";
        }

        private string BuildRecentUpgradeHistoryLabel()
        {
            if (_run.RecentUpgradePicks == null || _run.RecentUpgradePicks.Count == 0)
            {
                return "无";
            }

            return string.Join("  ||  ", _run.RecentUpgradePicks);
        }

        private string BuildEarlyCommonTtkLabel()
        {
            if (_run == null)
            {
                return "前期普通鼠锤数: 统计中";
            }

            float targetMin = GetEarlyCommonTtkMinHits();
            float targetMax = GetEarlyCommonTtkMaxHits();
            if (_run.EarlyCommonKillSamples <= 0)
            {
                return $"前期普通鼠锤数: 统计中 (目标 {targetMin:0.#}-{targetMax:0.#} 锤)";
            }

            return $"前期普通鼠锤数: {_run.EarlyCommonManualHitAverage:0.00} (样本 {_run.EarlyCommonKillSamples}, 目标 {targetMin:0.#}-{targetMax:0.#})";
        }

        private static string ShortFacilityName(FacilityType type)
        {
            return type switch
            {
                FacilityType.AutoHammerTower => "锤塔",
                FacilityType.SensorHammer => "雷锤",
                FacilityType.GoldMagnet => "吸金",
                FacilityType.BountyMarker => "赏金",
                FacilityType.TeslaCoupler => "电网",
                FacilityType.ExecutionPlate => "处决",
                _ => "设施",
            };
        }

        private string ResolveBuildIdentity()
        {
            int bountyLevel = _run.FacilityLevels.TryGetValue(FacilityType.BountyMarker, out int bounty) ? bounty : 0;
            int towerLevel = _run.FacilityLevels.TryGetValue(FacilityType.AutoHammerTower, out int tower) ? tower : 0;
            int sensorLevel = _run.FacilityLevels.TryGetValue(FacilityType.SensorHammer, out int sensor) ? sensor : 0;
            int teslaLevel = _run.FacilityLevels.TryGetValue(FacilityType.TeslaCoupler, out int tesla) ? tesla : 0;
            int executeLevel = _run.FacilityLevels.TryGetValue(FacilityType.ExecutionPlate, out int execute) ? execute : 0;
            if ((bountyLevel >= 2 || _run.BountyContractCount >= 2) && _run.BuildTags.Contains("Economy"))
            {
                return "赏金工厂";
            }

            if (sensorLevel >= 2 && _run.BuildTags.Contains("Chain"))
            {
                return "电链设施联动";
            }

            if ((teslaLevel >= 2 && _run.BuildTags.Contains("Chain")) ||
                (executeLevel >= 2 && _run.BuildTags.Contains("Execute")))
            {
                return "高压处决流";
            }

            if (towerLevel >= 2 || _run.ActiveFacilityCount >= 3)
            {
                return "自动锤阵";
            }

            if (_run.Stats.DroneCount >= 3 && _run.Stats.AutoHammerInterval > 0f)
            {
                return "无人机收割线";
            }

            if (_run.ElapsedSeconds <= 240f && !_run.BuildMilestoneReached)
            {
                return _run.OpeningRoute switch
                {
                    OpeningRouteAutoTower => "自动锤阵(预热)",
                    OpeningRouteChainGrid => "电链设施联动(预热)",
                    OpeningRouteBountyFactory => "赏金工厂(预热)",
                    _ => _run.BuildTags.Contains("Damage") ? "战斗混合流" : "未成型",
                };
            }

            return _run.BuildTags.Contains("Damage") ? "战斗混合流" : "未成型";
        }

        private string GetWeaponName(string weaponId)
        {
            WeaponDef def = _content.Weapons.FirstOrDefault(w => w.Id == weaponId);
            return def != null ? def.DisplayName : weaponId;
        }

        private string GetCharacterName(string characterId)
        {
            CharacterDef def = _content.Characters.FirstOrDefault(c => c.Id == characterId);
            return def != null ? def.DisplayName : characterId;
        }

        private void ShowMessage(string text, float duration, int priority = 1)
        {
            if (_messageTimer > 0f && priority < _messagePriority)
            {
                return;
            }

            _centerMessage.text = text;
            _messageTimer = duration;
            _messagePriority = Mathf.Clamp(priority, 0, 3);
            TriggerAlertFlash(priority);
        }

        private void ClearDrops()
        {
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                _drops[i].MarkCollected();
            }

            _drops.Clear();
        }
    }
}
