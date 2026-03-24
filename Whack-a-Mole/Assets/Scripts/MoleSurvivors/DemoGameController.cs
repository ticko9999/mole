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
        public MoleRuntime(MoleDef def, float hpScale)
        {
            Def = def;
            RemainingHp = def.BaseHp * hpScale;
            ShieldHp = def.Traits.HasFlag(MoleTrait.Shield) ? Mathf.Max(6f, Def.BaseHp * 0.35f) : 0f;
        }

        public MoleDef Def { get; }

        public float RemainingHp { get; private set; }

        public float ShieldHp { get; private set; }

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

        public bool CanInstallFacility => Facility == null;

        public float RareWeightMultiplier { get; private set; } = 1f;

        public float GoldRewardMultiplier { get; private set; } = 1f;

        public float LocalMagnetRadius { get; private set; }

        public bool CanSpawn => State == HoleState.Idle && CurrentMole == null;

        public bool HasLiveMole => CurrentMole != null && State == HoleState.HitWindow;

        public bool IsTargetable => CurrentMole != null && (State == HoleState.HitWindow || State == HoleState.HitFlash);

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
            if (facility == null)
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
            RareWeightMultiplier = Mathf.Max(1f, rareWeightMultiplier);
            GoldRewardMultiplier = Mathf.Max(1f, goldRewardMultiplier);
            LocalMagnetRadius = Mathf.Max(0f, localMagnetRadius);
            RefreshFacilityLabel();
        }

        public void Spawn(MoleDef def, float hpScale, float timingScale = 1f)
        {
            CurrentMole = new MoleRuntime(def, hpScale);
            _lastMoleDef = def;
            _timingScale = Mathf.Clamp(timingScale, 0.85f, 2f);
            State = HoleState.Warning;
            _timer = Mathf.Max(0.12f, def.WarningSeconds * _timingScale);
            RefreshVisual();
        }

        public void Tick(float deltaTime, Action<HoleRuntime> escapeCallback)
        {
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
    }

    public sealed class DemoGameController : MonoBehaviour
    {
        private const float DefaultRunDurationSeconds = 600f;
        private const float DefaultBossGraceSeconds = 60f;
        private const string DefaultSkinResourcePath = "MoleSurvivors/DefaultPresentationSkin";
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
        private ISpawnDirector _spawnDirector;
        private IAutomationService _automationService;
        private IFacilityService _facilityService;
        private IBossEncounterService _bossEncounterService;
        private AchievementService _achievementService;

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
        private List<UpgradeDef> _currentOffer = new List<UpgradeDef>();

        private GameObject _eventPanel;
        private Text _eventText;
        private Image _eventIcon;
        private Button _eventAcceptButton;
        private Button _eventSkipButton;
        private RunEventDef _pendingEvent;

        private GameObject _endPanel;
        private Text _endSummary;

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

        public bool EnableAutoPilotForTests { get; set; }

        public RunState CurrentRun => _run;

        public MetaProgressState MetaState => _meta;

        public bool BossSpawned => _run != null && _run.BossSpawned;

        public bool MidBossSpawned => _run != null && _run.MidBossSpawned;

        public PresentationSkin ActivePresentationSkin => _presentationSkin;

        public int ActiveFacilityCount => _holes.Count(h => h.Facility != null);

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
            ApplyConfigDrivenPresentationSettings();
            _saveRepository = new JsonSaveRepository(
                defaultUnlockedWeapons: _content.StartupUnlockedWeaponIds,
                defaultUnlockedCharacters: _content.StartupUnlockedCharacterIds,
                defaultWeaponId: ResolveConfiguredDefaultWeaponId(),
                defaultCharacterId: ResolveConfiguredDefaultCharacterId());
            _upgradeOfferService = new UpgradeOfferService();
            _spawnDirector = new SpawnDirector();
            _automationService = new AutomationService();
            _facilityService = new FacilityService();
            _bossEncounterService = new BossEncounterService();
            _achievementService = new AchievementService();

            _meta = _saveRepository.LoadOrCreate();
            NormalizeMetaStateAgainstContent();
            ResolvePresentationSkin();
            ResolveExternalUiSkin();

            EnsureCamera();
            EnsureAudioSource();
            EnsureEventSystem();
            BuildWorld();
            BuildUI();
            StartRun();
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
                ApplyConfigDrivenPresentationSettings();
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
                _endPanel = null;
                _metaPanel = null;
            }

            EnsureCamera();
            EnsureAudioSource();
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

            if (!_upgradeOpen && !_eventOpen && !_metaOpen && !_endOpen)
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
                    ResolveEvent(true);
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
            _sfxSource.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
            _sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume * volumeMultiplier));
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

        private void BuildHudMeters(Transform canvasRoot, RectTransform rightBar)
        {
            _bossBarRoot = CreateMeterRoot(
                "BossHudBar",
                canvasRoot,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(940f, 72f),
                new Vector2(0f, -124f));
            _bossBarBackground = CreateMeterLayer(
                "Bg",
                _bossBarRoot,
                _externalUiSkin != null ? _externalUiSkin.BossHpBackgroundSprite : null,
                new Color(0.05f, 0.07f, 0.1f, 0.84f),
                false);
            _bossBarShieldFill = CreateMeterLayer(
                "ShieldFill",
                _bossBarRoot,
                _externalUiSkin != null ? _externalUiSkin.BossHpShieldFillSprite : null,
                new Color(0.45f, 0.82f, 0.95f, 0.85f),
                true);
            _bossBarFill = CreateMeterLayer(
                "Fill",
                _bossBarRoot,
                _externalUiSkin != null ? _externalUiSkin.BossHpFillSprite : null,
                new Color(0.95f, 0.36f, 0.32f, 0.96f),
                true);
            _bossBarWarnGlow = CreateMeterLayer(
                "WarnGlow",
                _bossBarRoot,
                _externalUiSkin != null ? _externalUiSkin.BossHpWarnGlowSprite : null,
                new Color(1f, 0.28f, 0.2f, 0f),
                false);
            _bossBarFrame = CreateMeterLayer(
                "Frame",
                _bossBarRoot,
                _externalUiSkin != null ? _externalUiSkin.BossHpFrameSprite : null,
                new Color(1f, 1f, 1f, 0.75f),
                false);
            _bossBarLabel = CreateText(
                "BossHudLabel",
                _bossBarRoot,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                24,
                TextAnchor.MiddleCenter,
                Color.white);
            _bossBarLabel.rectTransform.offsetMin = new Vector2(18f, 0f);
            _bossBarLabel.rectTransform.offsetMax = new Vector2(-18f, 0f);
            _bossBarRoot.gameObject.SetActive(false);

            _durabilityBarRoot = CreateMeterRoot(
                "DurabilityBar",
                rightBar,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(336f, 34f),
                new Vector2(-16f, -24f));
            _durabilityBarFill = CreateMeterLayer(
                "Fill",
                _durabilityBarRoot,
                _externalUiSkin != null ? _externalUiSkin.DurabilityFillSprite : null,
                new Color(0.34f, 0.88f, 0.48f, 0.96f),
                true);
            _durabilityBarDangerOverlay = CreateMeterLayer(
                "Danger",
                _durabilityBarRoot,
                _externalUiSkin != null ? _externalUiSkin.DurabilityDangerOverlaySprite : null,
                new Color(0.95f, 0.22f, 0.2f, 0f),
                false);
            _durabilityBarFrame = CreateMeterLayer(
                "Frame",
                _durabilityBarRoot,
                _externalUiSkin != null ? _externalUiSkin.DurabilityFrameSprite : null,
                new Color(1f, 1f, 1f, 0.75f),
                false);
            _durabilityBarLabel = CreateText(
                "DurabilityLabel",
                _durabilityBarRoot,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                19,
                TextAnchor.MiddleCenter,
                Color.white);

            _expBarRoot = CreateMeterRoot(
                "ExpBar",
                rightBar,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(336f, 34f),
                new Vector2(-16f, -70f));
            _expBarFill = CreateMeterLayer(
                "Fill",
                _expBarRoot,
                _externalUiSkin != null ? _externalUiSkin.ExpBarFillSprite : null,
                new Color(0.36f, 0.86f, 0.95f, 0.96f),
                true);
            _expBarLevelFlash = CreateMeterLayer(
                "Flash",
                _expBarRoot,
                _externalUiSkin != null ? _externalUiSkin.ExpLevelFlashSprite : null,
                new Color(0.95f, 0.95f, 0.6f, 0f),
                false);
            _expBarFrame = CreateMeterLayer(
                "Frame",
                _expBarRoot,
                _externalUiSkin != null ? _externalUiSkin.ExpBarFrameSprite : null,
                new Color(1f, 1f, 1f, 0.74f),
                false);
            _expBarLabel = CreateText(
                "ExpLabel",
                _expBarRoot,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                19,
                TextAnchor.MiddleCenter,
                Color.white);

            _comboBarRoot = CreateMeterRoot(
                "ComboBar",
                rightBar,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(336f, 34f),
                new Vector2(-16f, -116f));
            _comboBarFill = CreateMeterLayer(
                "Fill",
                _comboBarRoot,
                _externalUiSkin != null ? _externalUiSkin.ComboBarFillSprite : null,
                new Color(0.98f, 0.72f, 0.24f, 0.96f),
                true);
            _comboBarMaxState = CreateMeterLayer(
                "Max",
                _comboBarRoot,
                _externalUiSkin != null ? _externalUiSkin.ComboBarMaxSprite : null,
                new Color(1f, 0.95f, 0.62f, 0f),
                false);
            _comboBarFrame = CreateMeterLayer(
                "Frame",
                _comboBarRoot,
                _externalUiSkin != null ? _externalUiSkin.ComboBarFrameSprite : null,
                new Color(1f, 1f, 1f, 0.74f),
                false);
            _comboBarLabel = CreateText(
                "ComboLabel",
                _comboBarRoot,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                19,
                TextAnchor.MiddleCenter,
                Color.white);
        }

        private static RectTransform CreateMeterRoot(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 size,
            Vector2 anchoredPosition)
        {
            GameObject rootGo = new GameObject(name, typeof(RectTransform));
            rootGo.transform.SetParent(parent, false);
            RectTransform root = rootGo.GetComponent<RectTransform>();
            root.anchorMin = anchorMin;
            root.anchorMax = anchorMax;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = size;
            root.anchoredPosition = anchoredPosition;
            return root;
        }

        private static Image CreateMeterLayer(
            string name,
            RectTransform parent,
            Sprite sprite,
            Color fallbackColor,
            bool filled)
        {
            GameObject layerGo = new GameObject(name, typeof(RectTransform), typeof(Image));
            layerGo.transform.SetParent(parent, false);
            RectTransform rect = layerGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = layerGo.GetComponent<Image>();
            image.raycastTarget = false;
            image.sprite = sprite != null ? sprite : SpriteCache.WhiteSprite;
            image.color = sprite != null ? Color.white : fallbackColor;
            image.type = filled ? Image.Type.Filled : Image.Type.Simple;
            image.preserveAspect = false;
            if (filled)
            {
                image.fillMethod = Image.FillMethod.Horizontal;
                image.fillOrigin = (int)Image.OriginHorizontal.Left;
                image.fillAmount = 1f;
            }

            return image;
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
                new Vector2(0.4f, 0.4f),
                new Vector2(300f, 100f),
                new Vector2(0f, -40f));
            _eventAcceptButton.GetComponentInChildren<Text>().text = "接受";
            _eventAcceptButton.onClick.AddListener(() => ResolveEvent(true));
            SetButtonIcon(_eventAcceptButton, _externalUiSkin != null ? _externalUiSkin.AcceptIconSprite : null, new Color(0.88f, 1f, 0.9f));

            _eventSkipButton = CreateButton(
                "EventSkip",
                _eventPanel.transform,
                new Vector2(0.6f, 0.4f),
                new Vector2(300f, 100f),
                new Vector2(0f, -40f));
            _eventSkipButton.GetComponentInChildren<Text>().text = "跳过";
            _eventSkipButton.onClick.AddListener(() => ResolveEvent(false));
            SetButtonIcon(_eventSkipButton, _externalUiSkin != null ? _externalUiSkin.SkipIconSprite : null, new Color(1f, 0.86f, 0.86f));
        }

        private void BuildEndPanel(Transform parent)
        {
            _endPanel = CreatePanel("EndPanel", parent, new Color(0f, 0f, 0f, 0.82f));
            _endPanel.SetActive(false);

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
            };

            ApplyLoadoutAndMeta();
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
            string artHint = _activeArtSummary.StartsWith("外部包", StringComparison.Ordinal)
                ? $"（{_activeArtSummary}）"
                : string.Empty;
            ShowMessage($"开始新一局：从点击敲鼠开始。{artHint}", 2.2f);
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
            _run.NextExperience = Mathf.Clamp(24f * runScale, 18f, 32f);
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

            _run.ActiveFacilityCount = _holes.Count(h => h.Facility != null);
            _rareHintCooldown = Mathf.Max(0f, _rareHintCooldown - deltaTime);
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
            TickDrops(deltaTime);
            TickBoss(deltaTime);
            TryOpenPendingUpgrade();
            CheckMilestones();
            _run.BuildIdentity = ResolveBuildIdentity();

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
            float clickRadius = Mathf.Max(_run.Stats.AttackRadius, isManual ? 0.68f : 0.52f);

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
                if (fallback != null && Vector2.Distance(fallback.Position, worldPoint) <= clickRadius * 2.2f)
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
                if (nearest != null && nearest.DistanceToVisualCenter(worldPoint) <= clickRadius * 2.4f)
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

                RegisterMiss();
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
                        DealDamageToHole(splashTarget, damage * 0.55f, AttackSource.Chain, false, false);
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
                    DealDamageToHole(chainTargets[i], damage * 0.62f, AttackSource.Chain, false, false);
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
            bool crit = false)
        {
            if (hole == null || !hole.IsTargetable || hole.CurrentMole == null)
            {
                return;
            }

            MoleRuntime mole = hole.CurrentMole;
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

            if (!result.Killed)
            {
                return;
            }

            MoleDef def = mole.Def;

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

            int goldReward = Mathf.RoundToInt(def.GoldReward * _run.Stats.GoldMultiplier * hole.GoldRewardMultiplier);
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

            int expReward = Mathf.RoundToInt(def.ExpReward * _run.Stats.ExpMultiplier);
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

            if (def.Traits.HasFlag(MoleTrait.Elite))
            {
                coreReward += 4;
                if (UnityEngine.Random.value <= 0.2f)
                {
                    _run.EventTickets += 1;
                    ShowMessage("精英掉落事件券", 0.9f, 2);
                }
            }

            SpawnDrop(DropType.Gold, goldReward, hole.Position + new Vector2(0f, 0.5f));
            SpawnDrop(DropType.Experience, expReward, hole.Position + new Vector2(0.2f, 0.35f));
            if (coreReward > 0)
            {
                SpawnDrop(DropType.Core, coreReward, hole.Position + new Vector2(-0.2f, 0.4f));
            }

            _run.CodexUnlockedThisRun.Add($"codex_{def.Id}");

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

            if (killed)
            {
                BossDef defeatedBoss = _boss.Def;
                bool finalEncounter = IsFinalBossEncounter(_activeBossEncounter);
                _run.CodexUnlockedThisRun.Add(GetBossCodexId(defeatedBoss));
                _run.CodexUnlockedThisRun.Add("codex_boss");
                int rewardGold = defeatedBoss != null ? defeatedBoss.RewardGold : 0;
                int rewardCore = defeatedBoss != null ? defeatedBoss.RewardCore : 0;
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
            _run.ComboTimer = _content != null
                ? Mathf.Max(0.2f, _content.ComboWindowSeconds)
                : 2.5f;
            _run.HighestCombo = Mathf.Max(_run.HighestCombo, _run.Combo);
            _run.TotalDamageEvents++;
            if (source == AttackSource.Manual && _run.Combo % 15 == 0)
            {
                ShowMessage($"{_run.Combo} 连击", 0.8f);
            }
        }

        private void RegisterMiss()
        {
            if (_run.Combo > 0)
            {
                _run.Combo = Mathf.Max(0, _run.Combo - 4);
            }

            _run.ComboTimer = _content != null
                ? Mathf.Max(0.1f, _content.ComboMissWindowSeconds)
                : 0.8f;
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
                    ? Mathf.Max(0.1f, _content.ComboDecayTickSeconds)
                    : 0.4f;
                _run.ComboTimer = _run.Combo > 0 ? decayTick : 0f;
            }
        }

        private void TickSpawnerAndHoles(float deltaTime)
        {
            for (int i = 0; i < _holes.Count; i++)
            {
                _holes[i].Tick(deltaTime, OnMoleEscaped);
            }

            float earlyEase = _run != null ? Mathf.Clamp01(_run.ElapsedSeconds / 210f) : 1f;
            int maxAttempts = HasActiveBoss() ? 2 : (_run != null && _run.ElapsedSeconds < 130f ? 1 : 2);
            float paceScale = Mathf.Lerp(0.36f, 1f, earlyEase);
            float spawnDelta = deltaTime * paceScale * (HasActiveBoss() ? Mathf.Clamp(_bossSpawnScale, 0.35f, 1f) : 1f);
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

                float earlyHpEase = Mathf.Lerp(0.62f, 1f, earlyEase);
                float lateScale = Mathf.Clamp01((_run.ElapsedSeconds - 160f) / 320f) * 0.78f;
                float hpScale = earlyHpEase + lateScale;
                float timingScale = Mathf.Lerp(1.75f, 1f, earlyEase);
                hole.Spawn(mole, hpScale, timingScale);

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
            RegisterMiss();

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

            drop.MarkCollected();
        }

        private void AddExperience(int amount)
        {
            _run.Experience += amount;
            while (_run.Experience >= _run.NextExperience)
            {
                _run.Experience -= _run.NextExperience;
                _run.Level++;
                _run.NextExperience = Mathf.Round(_run.NextExperience * 1.13f + 3f);
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
            }

            _upgradePanel.SetActive(true);
            _upgradeOpen = true;
        }

        private string BuildUpgradeOptionText(UpgradeDef def)
        {
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
            }

            ShowMessage("消耗事件券：升级方案已重构", 1.1f, 2);
        }

        private void ApplyUpgrade(UpgradeDef def)
        {
            if (def == null)
            {
                return;
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
            }

            if (_run.Stats.AutoHammerInterval > 0f &&
                _run.ElapsedSeconds >= GetRunDurationSeconds() * 0.15f)
            {
                _run.AutomationMilestoneReached = true;
            }

            if (_holes.Any(h => h.Facility != null))
            {
                _run.FacilityMilestoneReached = true;
            }

            CheckEvolution();
            UpgradeStatsSnapshot afterSnapshot = UpgradeStatsSnapshot.Capture(_run);
            string deltaSummary = UpgradePresentationFormatter.BuildAppliedDeltaLine(def, beforeSnapshot, afterSnapshot, _run);
            RecordUpgradeSelection(def, deltaSummary);
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
            _pendingEvent = runEvent;
            _eventOpen = true;
            _eventPanel.SetActive(true);
            _eventText.text = $"{runEvent.DisplayName}\n<size=25>{runEvent.Description}</size>";
            Sprite eventSprite = ResolveEventSprite(runEvent.Type);
            if (_eventIcon != null)
            {
                _eventIcon.sprite = eventSprite;
                _eventIcon.color = eventSprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
                _eventIcon.gameObject.SetActive(eventSprite != null);
            }

            _eventAcceptButton.GetComponentInChildren<Text>().text = runEvent.Type == RunEventType.MerchantBoost
                ? $"接受 (-{runEvent.GoldCost}G)"
                : "接受";
            _eventSkipButton.GetComponentInChildren<Text>().text = _run.EventTickets > 0
                ? $"重构 (-1券)"
                : "跳过";
            if (_presentationSkin != null)
            {
                PlayClip(_presentationSkin.EventAlertSfx, 0.92f, 0.02f);
            }
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

        private void ResolveEvent(bool accept)
        {
            if (!_eventOpen || _pendingEvent == null)
            {
                return;
            }

            if (!accept && _run.EventTickets > 0)
            {
                _run.EventTickets -= 1;
                RunEventDef reroll = SelectEventForCurrentStage(_pendingEvent.Id);
                if (reroll != null)
                {
                    ShowMessage("事件已重构", 1f, 2);
                    OpenEvent(reroll);
                    return;
                }
            }

            if (accept)
            {
                _run.EventParticipationCount++;
                switch (_pendingEvent.Type)
                {
                    case RunEventType.MerchantBoost:
                        if (_run.Gold >= _pendingEvent.GoldCost)
                        {
                            _run.Gold -= _pendingEvent.GoldCost;
                            UpgradeDef gift = _content.Upgrades
                                .Where(u => !_run.UpgradeStacks.ContainsKey(u.Id) || _run.UpgradeStacks[u.Id] < u.MaxStacks)
                                .OrderByDescending(u => u.Rarity)
                                .ThenBy(_ => UnityEngine.Random.value)
                                .FirstOrDefault();
                            if (gift != null)
                            {
                                ApplyUpgrade(gift);
                                ShowMessage("商人提供了额外强化", 1.2f);
                            }
                        }
                        else
                        {
                            ShowMessage("金币不足，商人离开了", 1f);
                        }

                        break;
                    case RunEventType.TreasureRush:
                        _run.TreasureRushRemaining = Mathf.Max(_run.TreasureRushRemaining, _pendingEvent.Value);
                        ShowMessage("暴富时刻开启", 1.2f);
                        break;
                    case RunEventType.CurseAltar:
                        _run.CurseRemaining = Mathf.Max(_run.CurseRemaining, _pendingEvent.Value);
                        ShowMessage("诅咒生效：刷新提速，收益提高", 1.4f, 2);
                        break;
                    case RunEventType.RepairStation:
                        _run.Durability = Mathf.Min(_run.MaxDurability, _run.Durability + Mathf.RoundToInt(_pendingEvent.Value));
                        bool boosted = _facilityService.BoostFacilitiesForRepair(_holes, 0.45f);
                        ShowMessage(boosted ? "维修站：耐久恢复并完成设施检修" : "农场耐久恢复", 1.2f);
                        break;
                    case RunEventType.BountyContract:
                        _run.BountyContractRemaining = Mathf.Max(_run.BountyContractRemaining, _pendingEvent.Value);
                        _run.BountyContractCount++;
                        ShowMessage("赏金合约生效：稀有目标概率提升", 1.25f, 2);
                        break;
                    case RunEventType.RogueHoleZone:
                        ActivateRogueZone(_pendingEvent.Value, 5);
                        ShowMessage("暴走洞区启动：高压高收益", 1.25f, 2);
                        break;
                }
            }

            string eventCodexId = GetEventCodexId(_pendingEvent);
            if (!string.IsNullOrWhiteSpace(eventCodexId))
            {
                _run.CodexUnlockedThisRun.Add(eventCodexId);
            }

            float progress = Mathf.Clamp01(_run.ElapsedSeconds / GetRunDurationSeconds());
            float minCooldown = Mathf.Lerp(95f, 62f, progress);
            float maxCooldown = Mathf.Lerp(130f, 92f, progress);
            _run.EventCooldown = UnityEngine.Random.Range(minCooldown, maxCooldown);
            _eventOpen = false;
            _eventPanel.SetActive(false);
            _pendingEvent = null;
        }

        private RunEventDef SelectEventForCurrentStage(string excludeId)
        {
            List<RunEventDef> candidates = _content.Events
                .Where(e => _run.ElapsedSeconds >= e.MinTime &&
                            _run.ElapsedSeconds <= e.MaxTime &&
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
                .OrderByDescending(h => h.DangerLevel * 2f + h.SpawnWeight)
                .ThenBy(h => h.Index)
                .Take(Mathf.Clamp(holeCount, 1, _holes.Count))
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
            _boss.Activate(hpScale);
            _boss.SetShieldActive(false);
            _bossSpawnScale = _bossEncounterService.ResolveSpawnScale(_activeBossEncounter);

            if (pending.Def.IsFinalBoss)
            {
                _run.BossSpawned = true;
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
            float automationSecond = runDuration * 0.15f;
            float facilitySecond = runDuration * 0.36f;
            float buildSecond = runDuration * 0.5f;
            float overdriveSecond = runDuration * 0.7f;

            if (!_run.AutomationMilestoneReached && _run.ElapsedSeconds >= automationSecond)
            {
                if (_run.Stats.AutoHammerInterval <= 0f && _run.Stats.DroneCount <= 0)
                {
                    _run.Stats.AutoHammerInterval = 1.55f;
                }

                _run.AutomationMilestoneReached = true;
                ShowMessage("自动化已上线，开始省力。", 1.4f);
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

                ShowMessage("工厂联动高频阶段开启。", 1.4f);
            }
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
            string achievementText = newlyUnlocked.Count == 0
                ? "无新成就"
                : string.Join("、", newlyUnlocked.Select(a => a.DisplayName));
            int totalCollectedGold = _run.ManualGoldCollected + _run.AutomationGoldCollected;
            float automationRatio = totalCollectedGold > 0
                ? (float)_run.AutomationGoldCollected / totalCollectedGold
                : 0f;
            string midBossStatus = _run.MidBossDefeated ? "已击败" : (_run.MidBossSpawned ? "未击败" : "未触发");
            string finalBossStatus = _run.BossDefeated ? "已击败" : (_run.BossSpawned ? "未击败" : "未触发");

            _endSummary.text =
                (win ? "胜利" : "失败") + "\n" +
                reason + "\n\n" +
                $"时长: {duration}s\n" +
                $"金币: {_run.Gold}\n" +
                $"核心: {_run.CoreShards}\n" +
                $"击杀: {_run.TotalKills}\n" +
                $"稀有击杀: {_run.RareKillCount}\n" +
                $"最高连击: {_run.HighestCombo}\n" +
                $"事件参与: {_run.EventParticipationCount}  剩余券: {_run.EventTickets}\n" +
                $"中期Boss: {midBossStatus}  终局Boss: {finalBossStatus}\n" +
                $"自动化贡献: {automationRatio * 100f:0}% ({_run.AutomationGoldCollected}/{Mathf.Max(1, totalCollectedGold)})\n" +
                $"单次收益峰值: {_run.PeakSingleIncome}\n" +
                $"流派标签: {_run.BuildIdentity}\n" +
                $"工坊芯片 +{workshopGain}\n" +
                $"新成就: {achievementText}\n\n" +
                "按下“工坊成长”可购买永久节点";
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

        private void UpdateHud()
        {
            if (_run == null)
            {
                return;
            }

            int minutes = Mathf.FloorToInt(_run.ElapsedSeconds / 60f);
            int seconds = Mathf.FloorToInt(_run.ElapsedSeconds % 60f);
            float runDuration = GetRunDurationSeconds();
            float stageManual = runDuration * 0.15f;
            float stageAuto = runDuration * 0.4f;
            float stageFacility = runDuration * 0.5f;
            float stageMidBoss = runDuration * 0.7f;
            string stage = _run.ElapsedSeconds switch
            {
                _ when _run.ElapsedSeconds < stageManual => "手动期",
                _ when _run.ElapsedSeconds < stageAuto => "半自动期",
                _ when _run.ElapsedSeconds < stageFacility => "设施展开期",
                _ when _run.ElapsedSeconds < stageMidBoss => "中期验收期",
                _ when _run.ElapsedSeconds < runDuration => "工厂暴走期",
                _ => "Boss 终局",
            };

            _topHud.text = $"{minutes:00}:{seconds:00}  |  阶段: {stage}";
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
            string recentUpgrades = BuildRecentUpgradeHistoryLabel();
            string lastGain = string.IsNullOrWhiteSpace(_run.LastUpgradeDeltaSummary)
                ? "最近增益: 无"
                : $"最近增益: {_run.LastUpgradeDeltaSummary}";

            _rightHud.text =
                $"金币: {_run.Gold}\n" +
                $"核心: {_run.CoreShards}\n" +
                $"等级: {_run.Level}\n" +
                $"经验: {Mathf.FloorToInt(_run.Experience)}/{Mathf.FloorToInt(_run.NextExperience)}\n" +
                $"耐久: {_run.Durability}/{_run.MaxDurability}\n" +
                $"连击: {_run.Combo}\n" +
                $"击杀: {_run.TotalKills}\n" +
                $"稀有击杀: {_run.RareKillCount}  事件券: {_run.EventTickets}\n" +
                $"设施: {_run.ActiveFacilityCount}  进度: {overloadState}\n" +
                $"{facilityMix}\n" +
                $"{facilityCd}\n" +
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
                $"{lastGain}\n" +
                $"已选升级(最近6): {recentUpgrades}\n" +
                $"美术: {_activeArtSummary}  |  UI: {_activeUiSummary}";
        }

        private void UpdateHudMeters()
        {
            if (_run == null)
            {
                return;
            }

            if (_bossBarRoot != null)
            {
                bool showBoss = HasActiveBoss();
                _bossBarRoot.gameObject.SetActive(showBoss);
                if (showBoss && _boss != null)
                {
                    float maxHp = Mathf.Max(1f, _boss.MaxHp);
                    float hpRatio = Mathf.Clamp01(_boss.RemainingHp / maxHp);
                    if (_bossBarFill != null)
                    {
                        _bossBarFill.fillAmount = hpRatio;
                    }

                    if (_bossBarShieldFill != null)
                    {
                        _bossBarShieldFill.fillAmount = hpRatio;
                        _bossBarShieldFill.enabled = _boss.ShieldActive;
                    }

                    if (_bossBarWarnGlow != null)
                    {
                        bool warn = hpRatio <= 0.35f;
                        float pulse = 0.55f + Mathf.Sin(Time.unscaledTime * 9f) * 0.45f;
                        _bossBarWarnGlow.color = new Color(1f, 1f, 1f, warn ? pulse : 0f);
                    }

                    if (_bossBarLabel != null)
                    {
                        string shield = _boss.ShieldActive ? " [护盾]" : string.Empty;
                        _bossBarLabel.text =
                            $"{_boss.Def.DisplayName}  {Mathf.CeilToInt(Mathf.Max(0f, _boss.RemainingHp))}/{Mathf.CeilToInt(maxHp)}{shield}";
                    }
                }
            }

            float durabilityRatio = _run.MaxDurability > 0
                ? Mathf.Clamp01((float)_run.Durability / _run.MaxDurability)
                : 0f;
            if (_durabilityBarFill != null)
            {
                _durabilityBarFill.fillAmount = durabilityRatio;
            }

            if (_durabilityBarDangerOverlay != null)
            {
                bool danger = durabilityRatio <= 0.35f;
                float pulse = 0.4f + Mathf.Sin(Time.unscaledTime * 8.5f) * 0.35f;
                _durabilityBarDangerOverlay.color = new Color(1f, 1f, 1f, danger ? Mathf.Clamp01(pulse) : 0f);
            }

            if (_durabilityBarLabel != null)
            {
                _durabilityBarLabel.text = $"耐久 {_run.Durability}/{_run.MaxDurability}";
            }

            float expRatio = _run.NextExperience > 0f
                ? Mathf.Clamp01(_run.Experience / _run.NextExperience)
                : 0f;
            if (_expBarFill != null)
            {
                _expBarFill.fillAmount = expRatio;
            }

            if (_expBarLabel != null)
            {
                _expBarLabel.text = $"经验 {Mathf.FloorToInt(_run.Experience)}/{Mathf.FloorToInt(_run.NextExperience)}";
            }

            _expBarFlashTimer = Mathf.Max(0f, _expBarFlashTimer - Time.unscaledDeltaTime);
            if (_expBarLevelFlash != null)
            {
                float flashAlpha = _expBarFlashTimer > 0f
                    ? Mathf.Clamp01(_expBarFlashTimer / 0.42f)
                    : 0f;
                _expBarLevelFlash.color = new Color(1f, 1f, 1f, flashAlpha);
            }

            float comboRatio = Mathf.Clamp01(_run.Combo / 30f);
            if (_comboBarFill != null)
            {
                _comboBarFill.fillAmount = comboRatio;
            }

            bool comboMax = _run.Combo >= 30;
            if (_comboBarMaxState != null)
            {
                float pulse = 0.45f + Mathf.Sin(Time.unscaledTime * 11f) * 0.4f;
                _comboBarMaxState.color = new Color(1f, 1f, 1f, comboMax ? Mathf.Clamp01(pulse) : 0f);
            }

            if (_comboBarLabel != null)
            {
                _comboBarLabel.text = comboMax ? $"连击 {_run.Combo} (MAX)" : $"连击 {_run.Combo}";
            }
        }

        private string BuildFacilityMixLabel()
        {
            int tower = _holes.Count(h => h.Facility != null && h.Facility.Type == FacilityType.AutoHammerTower);
            int sensor = _holes.Count(h => h.Facility != null && h.Facility.Type == FacilityType.SensorHammer);
            int magnet = _holes.Count(h => h.Facility != null && h.Facility.Type == FacilityType.GoldMagnet);
            int bounty = _holes.Count(h => h.Facility != null && h.Facility.Type == FacilityType.BountyMarker);
            return $"锤塔{tower} 雷锤{sensor} 吸金{magnet} 赏金{bounty}";
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

        private string BuildRecentUpgradeHistoryLabel()
        {
            if (_run.RecentUpgradePicks == null || _run.RecentUpgradePicks.Count == 0)
            {
                return "无";
            }

            return string.Join("  ||  ", _run.RecentUpgradePicks);
        }

        private static string ShortFacilityName(FacilityType type)
        {
            return type switch
            {
                FacilityType.AutoHammerTower => "锤塔",
                FacilityType.SensorHammer => "雷锤",
                FacilityType.GoldMagnet => "吸金",
                FacilityType.BountyMarker => "赏金",
                _ => "设施",
            };
        }

        private string ResolveBuildIdentity()
        {
            int bountyLevel = _run.FacilityLevels.TryGetValue(FacilityType.BountyMarker, out int bounty) ? bounty : 0;
            int towerLevel = _run.FacilityLevels.TryGetValue(FacilityType.AutoHammerTower, out int tower) ? tower : 0;
            int sensorLevel = _run.FacilityLevels.TryGetValue(FacilityType.SensorHammer, out int sensor) ? sensor : 0;
            if ((bountyLevel >= 2 || _run.BountyContractCount >= 2) && _run.BuildTags.Contains("Economy"))
            {
                return "赏金工厂";
            }

            if (sensorLevel >= 2 && _run.BuildTags.Contains("Chain"))
            {
                return "电链设施联动";
            }

            if (towerLevel >= 2 || _run.ActiveFacilityCount >= 3)
            {
                return "自动锤阵";
            }

            if (_run.Stats.DroneCount >= 3 && _run.Stats.AutoHammerInterval > 0f)
            {
                return "无人机收割线";
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
