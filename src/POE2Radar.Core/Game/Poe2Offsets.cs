namespace POE2Radar.Core.Game;

/// <summary>
/// PoE2 memory offsets — the going-forward source of truth, sourced from the GameHelper2
/// <c>GameOffsets/</c> dump and validated against the live client where marked ✓.
///
/// <para>This is separate from the legacy PoE1-shaped <see cref="KnownOffsets"/> (which the
/// overlay still references and which is being migrated). As each PoE2 structure is validated
/// here, the corresponding overlay reader is rechained to use it.</para>
///
/// Markers: ✓ = confirmed against live PoE2; (GH2) = from GameHelper2, not yet live-checked;
/// ✗ = transcribed from a third-party IDA dump (a private fork), NOT yet validated against our
/// live client and NOT yet wired into any read path. Validate via the Research probes before using
/// any ✗ offset — patch drift means these may be wrong for the current build.
/// </summary>
public static class Poe2
{
    /// <summary>Tile→world = 250, tile→grid = 23 ⇒ world/grid ratio ≈ 10.8696. ✓</summary>
    public static readonly float WorldToGridRatio = OffsetOverrides.Get("Poe2.WorldToGridRatio", 250f / 23f);

    /// <summary>Conservative network-bubble radius in grid units (GH2 uses 150). </summary>
    public static readonly int NetworkBubbleGrid = OffsetOverrides.Get("Poe2.NetworkBubbleGrid", 150);

    /// <summary>
    /// GameState root — found via the "Game States" AOB pattern (<see cref="AobPatterns"/>).
    /// Holds the array of game-state slots; one of them is InGameState.
    /// </summary>
    public static class GameState
    {
        public static readonly int CurrentStatePtr = OffsetOverrides.Get("GameState.CurrentStatePtr", 0x08);  // (GH2) StdVector — current state
        public static readonly int States          = OffsetOverrides.Get("GameState.States", 0x48);  // (GH2) inline array of 12 × StdTuple2D<IntPtr> (16 bytes each)
        public static readonly int StateSlotStride = OffsetOverrides.Get("GameState.StateSlotStride", 0x10);   // each slot is StdTuple2D<IntPtr> (ptr + extra)
        public static readonly int StateSlotCount  = OffsetOverrides.Get("GameState.StateSlotCount", 12);
    }

    /// <summary>
    /// InGameState. Resolve it from <c>GameState.CurrentStatePtr</c> (StdVector @ +0x08): the
    /// vector's first element is the active state pointer when in-game. ✓ (matches States[] slot).
    /// </summary>
    public static class InGameState
    {
        public static readonly int AreaInstanceData = OffsetOverrides.Get("InGameState.AreaInstanceData", 0x2A0); // ✓ → AreaInstance (validated: target holds the local player). 2026-09-04 patch shifted +0x10 (was 0x290).
        public static readonly int UiRoot           = OffsetOverrides.Get("InGameState.UiRoot", 0x300); // ✓ → root UiElement (self-ref; children are UI elements). 2026-09-04 patch shifted +0x10 (was 0x2F0).
        public static readonly int Camera           = OffsetOverrides.Get("InGameState.Camera", 0x378); // ✓ → Camera object (Zoom @ +0x528 == 1.0 confirmed). 2026-09-04 patch shifted +0x10 (was 0x368).
        public static readonly int WorldData        = OffsetOverrides.Get("InGameState.WorldData", 0x310); // (GH2-drift) → WorldData (area name + camera) — TBD
        public static readonly int UiRootStructPtr  = OffsetOverrides.Get("InGameState.UiRootStructPtr", 0x340); // (GH2-drift) reads 0 here — TBD
    }

    public static class UiRootStruct
    {
        public static readonly int UiRootPtr = OffsetOverrides.Get("UiRootStruct.UiRootPtr", 0x5A8); // (GH2)
        public static readonly int GameUiPtr = OffsetOverrides.Get("UiRootStruct.GameUiPtr", 0xBF0); // (GH2)
    }

    /// <summary>
    /// The big per-area container: area metadata, player, entity maps, terrain.
    /// <para>⚠ GameHelper2's internal offsets are DRIFTED in this build — confirmed by the live
    /// probe (PlayerInfo moved from GH2's 0xA00 to ~0x598; LocalPlayer at 0x5B8). The values
    /// marked (GH2-drift) below must be re-discovered (see <c>--find-entities</c> / <c>--find-terrain</c>).</para>
    ///
    /// <para><b>This struct does NOT move as one block.</b> The 2026-09-04 patch shifted the
    /// EARLY fields (AreaInfoPtr / AreaLevel / AreaHash) by <b>-0x08</b> while the LATE block
    /// (ServerData / LocalPlayer / entity maps / terrain) moved <b>+0x10</b> — opposite
    /// directions in the same struct. Re-derive each field independently with
    /// <c>POE2Radar.Research --areascan</c>; never extrapolate one field's delta onto another.</para>
    /// </summary>
    public static class AreaInstance
    {
        public static readonly int AreaInfoPtr      = OffsetOverrides.Get("AreaInstance.AreaInfoPtr", 0x098);  // ✓ → AreaInfo; +0x00 → UTF-16 "Code\0Name\0" (Code validated 'G1_15'). 2026-09-04 patch shifted -0x08 (was 0x0A0).
        public static readonly int LocalPlayer      = OffsetOverrides.Get("AreaInstance.LocalPlayer", 0x5D0);  // ✓ → player Entity (value-scanned player matched here). 2026-09-04 patch shifted +0x10 (was 0x5C0); 2026-07-16 shifted +0x08 (was 0x5B8); 2026-06-25 shifted +0x18 (was 0x5A0).
        public static readonly int ServerDataPtr    = OffsetOverrides.Get("AreaInstance.ServerDataPtr", 0x5B0);  // ✓ → ServerData (gateway to player inventories; +0x20 here = LocalPlayer @ 0x5D0). 2026-09-04 patch shifted +0x10 (was 0x5A0); 2026-07-16 shifted +0x08 (was 0x598); 2026-06-25 shifted +0x18 (was 0x580).
        public static readonly int AwakeEntities    = OffsetOverrides.Get("AreaInstance.AwakeEntities", 0x6F0);  // ✓ StdMap of live entities (id→EntityPtr). 2026-09-04 patch shifted +0x10 (was 0x6E0); 2026-07-16 shifted +0x08 (was 0x6D8); 2026-06-25 shifted +0x18 (was 0x6C0).
        public static readonly int SleepingEntities = OffsetOverrides.Get("AreaInstance.SleepingEntities", 0x700);  // ✓ StdMap. 2026-09-04 patch shifted +0x10 (was 0x6F0); 2026-07-16 shifted +0x08 (was 0x6E8); 2026-06-25 shifted +0x18 (was 0x6D0).
        public static readonly int TerrainMetadata  = OffsetOverrides.Get("AreaInstance.TerrainMetadata", 0x8D0);  // ✓ TerrainStruct base. 2026-09-04 patch shifted +0x10 (was 0x8C0); 2026-07-16 shifted +0x08 (was 0x8B8); 2026-06-25 shifted +0x18 (was 0x8A0).
        public static readonly int CurrentAreaLevel = OffsetOverrides.Get("AreaInstance.CurrentAreaLevel", 0x0BC);  // ✓ int — per-area, validated 15 in G1_15. 2026-09-04 patch shifted -0x08 (was 0x0C4).
        public static readonly int CurrentAreaHash  = OffsetOverrides.Get("AreaInstance.CurrentAreaHash", 0x114);  // ✓ uint — per-area random hash (+0x118 paired seed). 2026-09-04 patch shifted -0x08 (was 0x11C).
    }

    /// <summary>Entity StdMap conventions. Maps live at AreaInstance+0x6C0 (Awake) / +0x6D0 (Sleeping).</summary>
    public static class EntityList
    {
        public static readonly int StdMapSize = OffsetOverrides.Get("EntityList.StdMapSize", 0x10); // each StdMap is {Head ptr, int Size, pad} = 16 bytes
        /// <summary>Entity ids below this are real entities; above are visuals/decorations (GH2 filter). ✓ confirmed live.</summary>
        public static readonly uint VisualIdThreshold = OffsetOverrides.Get("EntityList.VisualIdThreshold", 0x40000000u);
    }

    /// <summary>std::map node: Left/Parent/Right ptrs, Color, IsNil byte, then Data{Key,Value} @ +0x20.</summary>
    public static class StdMapNode
    {
        public static readonly int Left   = OffsetOverrides.Get("StdMapNode.Left", 0x00);
        public static readonly int Parent = OffsetOverrides.Get("StdMapNode.Parent", 0x08);
        public static readonly int Right  = OffsetOverrides.Get("StdMapNode.Right", 0x10);
        public static readonly int IsNil  = OffsetOverrides.Get("StdMapNode.IsNil", 0x19); // bool
        public static readonly int Data   = OffsetOverrides.Get("StdMapNode.Data", 0x20); // Key (EntityNodeKey: uint id + pad = 8 bytes), then Value (IntPtr EntityPtr)
        public static readonly int KeyId  = OffsetOverrides.Get("StdMapNode.KeyId", 0x20); // uint entity id
        public static readonly int ValueEntityPtr = OffsetOverrides.Get("StdMapNode.ValueEntityPtr", 0x28); // IntPtr
    }

    /// <summary>An Entity object.</summary>
    public static class Entity
    {
        public static readonly int EntityDetailsPtr = OffsetOverrides.Get("Entity.EntityDetailsPtr", 0x08); // ✓ → EntityDetails
        public static readonly int ComponentList    = OffsetOverrides.Get("Entity.ComponentList", 0x10); // ✓ StdVector of component pointers (8-byte elems)
        public static readonly int Id               = OffsetOverrides.Get("Entity.Id", 0x80); // (GH2) uint  (read 0 for local player — revisit)
        public static readonly int IsValid          = OffsetOverrides.Get("Entity.IsValid", 0x84); // (GH2) byte; valid when bit0 clear
    }

    public static class EntityDetails
    {
        public static readonly int Name              = OffsetOverrides.Get("EntityDetails.Name", 0x08); // ✓ StdWString — metadata path (e.g. Metadata/Characters/<Class>/<Variant>)
        public static readonly int ComponentLookUpPtr = OffsetOverrides.Get("EntityDetails.ComponentLookUpPtr", 0x28); // ✓ → ComponentLookUp
    }

    /// <summary>ComponentLookUp: a StdBucket of (NamePtr, Index) at +0x28; index → ComponentList[index].</summary>
    public static class ComponentLookUp
    {
        public static readonly int NameAndIndexBucket = OffsetOverrides.Get("ComponentLookUp.NameAndIndexBucket", 0x28); // ✓ StdBucket; its Data StdVector starts here
        public static readonly int EntryStride        = OffsetOverrides.Get("ComponentLookUp.EntryStride", 0x10); // ✓ {IntPtr NamePtr; int Index; int pad}
    }

    // ── Components (offsets from the component object base) ───────────────────

    /// <summary>Life — ✓ re-validated live 2026-06-04 after the patch (980/980 HP, 427 mana, 274 ES).
    /// The vital blocks slid (each grew ~8 bytes): Health 0x1A8→0x1B0, Mana 0x1F8→0x208, ES 0x230→0x248.
    /// The VitalStruct's internal layout (Max@+0x2C, Current@+0x30) was UNCHANGED — only these
    /// per-vital offsets moved. (Prior build: 442/442 HP, 271 mana, 186/186 ES at 0x1A8/0x1F8/0x230.)</summary>
    public static class Life
    {
        public static readonly int Owner        = OffsetOverrides.Get("Life.Owner", 0x008); // ComponentHeader.EntityPtr (back-pointer to entity)
        public static readonly int Health       = OffsetOverrides.Get("Life.Health", 0x1B0); // ✓ VitalStruct (was 0x1A8 pre-patch)
        public static readonly int Mana         = OffsetOverrides.Get("Life.Mana", 0x208); // ✓ VitalStruct (was 0x1F8 pre-patch)
        public static readonly int EnergyShield = OffsetOverrides.Get("Life.EnergyShield", 0x248); // ✓ VitalStruct (was 0x230 pre-patch)
    }

    /// <summary>VitalStruct — ✓ (Max/Current confirmed). Reuse <see cref="VitalStruct"/> for reads.</summary>
    public static class Vital
    {
        public static readonly int ReservedFlat = OffsetOverrides.Get("Vital.ReservedFlat", 0x10);
        public static readonly int Regen        = OffsetOverrides.Get("Vital.Regen", 0x28);
        public static readonly int Max          = OffsetOverrides.Get("Vital.Max", 0x2C); // ✓
        public static readonly int Current      = OffsetOverrides.Get("Vital.Current", 0x30); // ✓
    }

    /// <summary>Render component.</summary>
    public static class Render
    {
        public static readonly int CurrentWorldPosition = OffsetOverrides.Get("Render.CurrentWorldPosition", 0x138); // ✓ Vector3 (X,Y,Z); grid = XY / WorldToGridRatio
        public static readonly int ModelBounds          = OffsetOverrides.Get("Render.ModelBounds", 0x144); // candidate (3 floats right after world pos)
    }

    /// <summary>Player component — character name + level. ✓ validated (name StdWString, level byte 27).</summary>
    public static class PlayerComponent
    {
        public static readonly int Name  = OffsetOverrides.Get("PlayerComponent.Name", 0x1B0); // ✓ StdWString
        public static readonly int Level = OffsetOverrides.Get("PlayerComponent.Level", 0x204); // ✓ byte (low byte of a u32 slot)
    }

    /// <summary>Camera object (at InGameState+0x368). Holds the WorldToScreen matrix.</summary>
    public static class Camera
    {
        // The matrix is stored duplicated (two identical 0x40-byte copies back-to-back); the first
        // copy is at +0x1A0. Row-major Matrix4x4; screen = project(world * M). Validated visually.
        public static readonly int WorldToScreenMatrix = OffsetOverrides.Get("Camera.WorldToScreenMatrix", 0x1A0);
        public static readonly int Zoom = OffsetOverrides.Get("Camera.Zoom", 0x528); // float, == 1.0 confirmed
    }

    /// <summary>MinimapIcon component — present on entities the game marks as map POIs (waypoints,
    /// checkpoints, league encounters…). <see cref="CompletedState"/> is an int the game flips when a
    /// repeatable encounter is finished: it then FADES the icon rather than removing it. ✓ validated
    /// live on an Expedition2Encounter — 0 while not-started/ready/active/looting, 1 after the reward
    /// was claimed. Read it live (don't cache the value): the component stays put; only the flag flips.</summary>
    public static class MinimapIcon
    {
        public static readonly int CompletedState = OffsetOverrides.Get("MinimapIcon.CompletedState", 0x10); // ✓ int — 0 = active/shown, non-zero = completed/faded
    }

    /// <summary>StateMachine component — drives stateful devices. Its listener vector at
    /// <see cref="ListenerVec"/> registers the device's RuneStation (see <see cref="RuneStation"/>).</summary>
    public static class StateMachine
    {
        public static readonly int ListenerVec = OffsetOverrides.Get("StateMachine.ListenerVec", 0x20); // ✓ StdVector {first,last} of listener-node ptrs
    }

    /// <summary>RuneStation — the heap object behind a runeshape-monolith device (the persistent
    /// <c>Metadata/MiscellaneousObjects/Expedition2/Expedition2Encounter</c> entity, the one carrying the
    /// MinimapIcon POI). NOT an entity/component: it's reached from the device via
    /// device→StateMachine→listener-vec → <c>station = *(node) − <see cref="ListenerSub"/></c>, verified by
    /// <c>*(station + <see cref="Owner"/>) == device</c>. Exposes the monolith's hole count + anchor rune
    /// WITHOUT opening the panel (and persists out of the network bubble → readable area-wide).
    /// ✓ validated live 2026-06-20 (Research <c>--monolith</c>): N=3, anchor rune index 12 ("Cyclonic").</summary>
    public static class RuneStation
    {
        public static readonly int Owner       = OffsetOverrides.Get("RuneStation.Owner", 0x10); // ✓ → device entity (verification)
        public static readonly int AnchorRef   = OffsetOverrides.Get("RuneStation.AnchorRef", 0x28); // ✓ → Expedition2Runes row ptr (0 = no anchor → "unique" monolith)
        public static readonly int AnchorHolder= OffsetOverrides.Get("RuneStation.AnchorHolder", 0x30); // ✓ → holder; (+0x28 → rune-table ptr; *ptr = per-area table base)
        public static readonly int HoleCount   = OffsetOverrides.Get("RuneStation.HoleCount", 0x38); // ✓ int N — the authoritative recipe hole count ("slots")
        public static readonly int AnchorPos   = OffsetOverrides.Get("RuneStation.AnchorPos", 0x3c); // ✓ int — anchor hole index (0-based)
        public static readonly int ListenerSub = OffsetOverrides.Get("RuneStation.ListenerSub", 0xA0); // ✓ listener node ptr = station + 0xA0 (2026-06-25 patch shifted +0x08, was 0x98; re-validated live: N=4 Tidal @ hole 3)
        public static readonly int RuneStride  = OffsetOverrides.Get("RuneStation.RuneStride", 0x68); // ✓ Expedition2Runes row stride (anchorIdx = (rowPtr-base)/stride). 2026-06-25 patch: 0x6c→0x68 (re-validated: delta 0x5B0/0x68 = 14 = Tidal)
        public static readonly int RuneCount   = OffsetOverrides.Get("RuneStation.RuneCount", 34);   // ✓ Expedition2Runes rows 0..33
    }

    /// <summary>ObjectMagicProperties component — monster/chest rarity.</summary>
    public static class ObjectMagicProperties
    {
        // ✓ validated live across 21 monsters (values 0 and 2 seen). Enum: 0=Normal,1=Magic,2=Rare,3=Unique.
        public static readonly int Rarity = OffsetOverrides.Get("ObjectMagicProperties.Rarity", 0x144);

        // ⚠ affix-mod vector (the rolled monster modifiers — auras/buffs like MonsterPhysicalDamageAura1).
        // std::vector at +0x168; element stride 0x20, record pointer at element+0x8, mod-id UTF-16 string
        // at record+0x0. Validated live 2026-06-11 across Magic/Rare/Unique (Research --mods); the seed
        // matched what the brute-force discovery found on every monster. NOT yet ✓-tier — one patch's
        // evidence — and patch-volatile, so the overlay reads it but Research --mods re-discovers on drift.
        // (+0x150 is the rarity/tier PLACEHOLDER vector — MonsterRare/Magic/Unique{N} filler — not affixes.)
        public static readonly int Mods = OffsetOverrides.Get("ObjectMagicProperties.Mods", 0x168);
        public static readonly int ModElemStride = OffsetOverrides.Get("ObjectMagicProperties.ModElemStride", 0x20);
        public static readonly int ModRecordPtr = OffsetOverrides.Get("ObjectMagicProperties.ModRecordPtr", 0x8);   // element + this → mod record pointer
        public static readonly int ModIdString = OffsetOverrides.Get("ObjectMagicProperties.ModIdString", 0x0);    // record + this → POINTER to the UTF-16 mod id (always deref, even when 0)
    }

    /// <summary>WorldItem component — wraps a dropped item on the ground. ⚠ validated live 2026-06-12
    /// (Research --item) on a dropped unique staff: the container entity is "Metadata/MiscellaneousObjects/
    /// WorldItem"; its WorldItem component +0x28 points to the actual item entity (its own
    /// EntityDetails/ComponentList, metadata "Metadata/Items/...").</summary>
    public static class WorldItemComponent
    {
        public static readonly int ItemEntity = OffsetOverrides.Get("WorldItemComponent.ItemEntity", 0x28); // ⚠ → inner item entity
    }

    /// <summary>RenderItem component (on the inner item entity) — the item's 2D art. ⚠ validated live
    /// 2026-06-12: +0x28 is a pointer to the UTF-16 .dds resource path (e.g.
    /// "Art/2DItems/Weapons/.../Uniques/Earthbound.dds"). The basename ("Earthbound") is the price-lookup
    /// key — it matches poe2scout's IconUrl basename. NB: RenderItem also lists socketed-gem art at later
    /// offsets, so take the FIRST entry (the item's own art).</summary>
    public static class RenderItemComponent
    {
        public static readonly int ResourcePath = OffsetOverrides.Get("RenderItemComponent.ResourcePath", 0x28); // ⚠ → UTF-16 .dds art path
    }

    /// <summary>Base component (on the inner item entity) — the item's BASE TYPE, including the rendered
    /// display name. ✓ validated live 2026-06-20 (Research --itemdump on a dropped Greater Orb of
    /// Augmentation): <c>Base +0x10</c> → a row whose <c>+0x30</c> is a pointer to the UTF-16 display name
    /// ("Greater Orb of Augmentation"); <c>Base +0x18</c> → the BaseItemTypes row (+0x00 internal id
    /// "CurrencyAddModToMagic2", +0x08 .dds art, +0x10 .ao). The display name is the price-lookup key for
    /// NON-uniques (currency/runes/essences/…), which the shared .dds art can't disambiguate across tiers.</summary>
    public static class BaseComponent
    {
        public static readonly int NameRow        = OffsetOverrides.Get("BaseComponent.NameRow", 0x10); // → row carrying the rendered display name
        public static readonly int RowDisplayName = OffsetOverrides.Get("BaseComponent.RowDisplayName", 0x30); // row + this → UTF-16 display base-type name
    }

    /// <summary>Mods component (on items) — rarity lives at a DIFFERENT offset than ObjectMagicProperties.
    /// ⚠ validated live 2026-06-12 on a dropped unique (read 3 = Unique). Matches GameHelper2's
    /// ModsAndObjectMagicProperties (Rarity at the sub-struct's +0x94; for the item Mods component the
    /// sub-struct is at +0x00, so rarity = +0x94). Enum 0=Normal,1=Magic,2=Rare,3=Unique.</summary>
    public static class ModsComponent
    {
        public static readonly int Rarity = OffsetOverrides.Get("ModsComponent.Rarity", 0x94);     // ✓ int (0=Normal,1=Magic,2=Rare,3=Unique)
        public static readonly int Identified = OffsetOverrides.Get("ModsComponent.Identified", 0x90); // ✓ int — 1 = identified, 0 = unidentified. Validated live
                                            // 2026-06-12 by diffing an identified unique (Earthbound=1) vs
                                            // an unidentified one (Keelhaul=0) on the ground.
        // Affix mod vectors — AllModsType (GH2) lives at the sub-struct's +0xA0, each a StdVector of
        // ModArrayStruct (stride 0x40). A record's ModsPtr (+0x28) → Mods.dat row whose first qword →
        // UTF-16 internal mod id ("UniqueGiantsBlood1"). ✓ validated live 2026-06-16 against the
        // identified unique gloves "Treefingers Riveted Mitts" (read UniqueGiantsBlood1 + 5 more) and
        // equipped rares/uniques (explicit + implicit ids matched the worn gear).
        public static readonly int ImplicitMods = OffsetOverrides.Get("ModsComponent.ImplicitMods", 0xA0); // ✓ StdVector<ModArrayStruct>
        public static readonly int ExplicitMods = OffsetOverrides.Get("ModsComponent.ExplicitMods", 0xB8); // ✓ StdVector<ModArrayStruct>
        public static readonly int EnchantMods  = OffsetOverrides.Get("ModsComponent.EnchantMods", 0xD0); // ✓ StdVector<ModArrayStruct>
        public static readonly int ModArrayStride = OffsetOverrides.Get("ModsComponent.ModArrayStride", 0x40); // ✓ sizeof(ModArrayStruct)
        public static readonly int ModRecordPtr   = OffsetOverrides.Get("ModsComponent.ModRecordPtr", 0x28); // ✓ element + this → Mods.dat row
        public static readonly int ModRecordIdPtr = OffsetOverrides.Get("ModsComponent.ModRecordIdPtr", 0x00); // ✓ row's first qword → UTF-16 internal mod id
    }

    /// <summary>Stack component (on stackable items) — current stack count. ✓ validated live 2026-06-16
    /// (currency/gem stacks in the player inventory read their true counts; matches GH2 StackOffsets).</summary>
    public static class StackComponent
    {
        public static readonly int Count = OffsetOverrides.Get("StackComponent.Count", 0x18); // ✓ int — current stack size
    }

    /// <summary>Player inventory chain. ✓ validated live 2026-06-16 (--inventory): every inventory
    /// (equipment + backpack + flasks + stash-style) resolved with correct box dimensions and items.
    /// Chain: AreaInstance +0x580 → ServerData; ServerData +0x48 → StdVector PlayerServerData, [0] →
    /// ServerDataStructure; ServerDataStructure +0x320 → StdVector PlayerInventories (InventoryArrayStruct,
    /// stride 0x18). Each InventoryArrayStruct: +0x00 int InventoryId (Inventories.dat index: 1=Main,
    /// 2=BodyArmour, 3=Weapon1, 5=Helm, 6=Amulet, 7/8=Rings, 9=Gloves, 10=Boots, 11=Belt, 12=Flask…),
    /// +0x08 ptr InventoryStruct, +0x10 ptr (= +0x08 − 0x10, the fingerprint invariant).</summary>
    public static class ServerData
    {
        public static readonly int League = OffsetOverrides.Get("ServerData.League", 0x2160);  // ✓ live 2026-09-04 (--areascan, read "Forbidden Rites") — std::wstring current league name, EXACTLY poe.ninja/poe2scout's Value (e.g. "HC Runes of Aldur", "Standard", "Hardcore"). The HC/SC prefix lets us auto-detect the price league. 2026-09-04 patch shifted -0x80 (was 0x21E0).
        public static readonly int PlayerServerDataVec = OffsetOverrides.Get("ServerData.PlayerServerDataVec", 0x48);  // ✓ StdVector<IntPtr>; [0] → ServerDataStructure
        public static readonly int PlayerInventoriesVec = OffsetOverrides.Get("ServerData.PlayerInventoriesVec", 0x320); // ✓ (on ServerDataStructure) StdVector<InventoryArrayStruct>
        public static readonly int InvArrayStride = OffsetOverrides.Get("ServerData.InvArrayStride", 0x18);        // ✓ sizeof(InventoryArrayStruct)
        public static readonly int InvArrayId     = OffsetOverrides.Get("ServerData.InvArrayId", 0x00);        // ✓ int InventoryName index
        public static readonly int InvArrayPtr    = OffsetOverrides.Get("ServerData.InvArrayPtr", 0x08);        // ✓ → InventoryStruct
    }

    /// <summary>InventoryStruct — one grid inventory. ✓ validated live 2026-06-16. TotalBoxes (X,Y) at
    /// +0x150; ItemList (StdVector of InventoryItemStruct pointers, length = X·Y) at +0x170.</summary>
    public static class Inventory
    {
        public static readonly int TotalBoxesX = OffsetOverrides.Get("Inventory.TotalBoxesX", 0x150); // ✓ int columns
        public static readonly int TotalBoxesY = OffsetOverrides.Get("Inventory.TotalBoxesY", 0x154); // ✓ int rows
        public static readonly int ItemListVec = OffsetOverrides.Get("Inventory.ItemListVec", 0x170); // ✓ StdVector<IntPtr→InventoryItemStruct>
        public static readonly int ServerRequestCounter = OffsetOverrides.Get("Inventory.ServerRequestCounter", 0x1E8); // (GH2) int
    }

    /// <summary>InventoryItemStruct — links a grid slot to an item entity. ✓ validated live 2026-06-16.
    /// Duplicate Item pointers across cells = a multi-cell item (de-dup by item address).</summary>
    public static class InventoryItem
    {
        public static readonly int Item      = OffsetOverrides.Get("InventoryItem.Item", 0x00); // ✓ → item Entity (ItemBase/ComponentList; meta "Metadata/Items/…")
        public static readonly int SlotStartX = OffsetOverrides.Get("InventoryItem.SlotStartX", 0x08); // ✓ int
        public static readonly int SlotStartY = OffsetOverrides.Get("InventoryItem.SlotStartY", 0x0C); // ✓ int
        public static readonly int SlotEndX   = OffsetOverrides.Get("InventoryItem.SlotEndX", 0x10); // ✓ int
        public static readonly int SlotEndY   = OffsetOverrides.Get("InventoryItem.SlotEndY", 0x14); // ✓ int
    }

    /// <summary>Sockets component (on socketable items) — socketed runes/soul-cores/gems as item-entity
    /// pointers. ⚠ one observation 2026-06-16 (--itemdump on a rare body armour with 2 Lesser Life Runes):
    /// owner back-ptr at +0x08 (ComponentHeader.EntityPtr); the two socketed RuneLifeLesser entities read
    /// as consecutive inline pointers at +0x30 / +0x38. Whether that's a fixed inline array or a small-buffer
    /// StdVector — and the empty-socket representation — needs cross-validation on items with other socket
    /// counts (the lone +0x98 hit was likely an unrelated neighbour pointer).</summary>
    public static class SocketsComponent
    {
        public static readonly int Owner          = OffsetOverrides.Get("SocketsComponent.Owner", 0x08); // ComponentHeader.EntityPtr
        public static readonly int SocketedItems  = OffsetOverrides.Get("SocketsComponent.SocketedItems", 0x30); // ⚠ first socketed item entity ptr (then +0x38, …)
    }

    /// <summary>Stats / LocalStats component — aggregated stat (key,value) pairs. ⚠ observed 2026-06-16.
    /// A StatArrayStruct is {int statIndex; int value}; the vector of them was found at +0x20 on an item's
    /// LocalStats component (read [131 = 18] = +18 local Energy Shield on the body armour). statIndex maps
    /// 1:1 to GameHelper2's GameStats enum (value = Stats.dat row index + 1, e.g. 131 = local_energy_shield),
    /// and that enum's ordering MATCHES our live build — so statIndex → stat-id string is solved via a ported
    /// GameStats table. NB: only LOCAL stats live on an item; global mods (life/resist) only aggregate onto
    /// the character's Stats component once equipped. GH2 chain for the character Stats component:
    /// +0x160 → StatsStructInternal, Stats StdVector @ +0xF8 (StatArrayStruct stride 0x08).</summary>
    public static class StatsComponent
    {
        public static readonly int StatArrayStride = OffsetOverrides.Get("StatsComponent.StatArrayStride", 0x08); // ✓ {int statIndex; int value}
        // ⚠ item LocalStats: a {key,value} StdVector observed at component +0x20 (one entry). Character
        // Stats: StatsChangedByItemsPtr @ +0x160 → StatsStructInternal; its Stats vec @ +0xF8 (GH2).
        public static readonly int ItemLocalStatsVec = OffsetOverrides.Get("StatsComponent.ItemLocalStatsVec", 0x20);  // ⚠ (one observation)
        public static readonly int StatsChangedByItemsPtr = OffsetOverrides.Get("StatsComponent.StatsChangedByItemsPtr", 0x160); // (GH2) → StatsStructInternal
        public static readonly int StatsStructStatsVec     = OffsetOverrides.Get("StatsComponent.StatsStructStatsVec", 0xF8);  // (GH2) StdVector<StatArrayStruct>
    }

    /// <summary>Chest component. ✓ OpenState @ +0x168 — the offset is stable, but the 2026-06-06 patch
    /// INVERTED its polarity: now 0 = closed/openable, non-zero = opened/used (was 1=closed/0=opened,
    /// per the 2026-06-03 read). Re-validated live by diffing a rare chest closed-vs-opened (+0x168
    /// flipped 0→1). The fork's extra sub-offsets did NOT survive validation on our build.</summary>
    public static class ChestComponent
    {
        public static readonly int OpenState       = OffsetOverrides.Get("ChestComponent.OpenState", 0x168); // ✓ 0 = closed/openable, non-zero = opened/used (polarity flipped 2026-06-06)
        // ⚠ INVALID on our build (live 2026-06-03, G3_3): 0x20/0x21/0x25 read 184/7/127 — identical
        // across a magic AND a normal chest, sitting inside pointer bytes (component header). The
        // fork's IDA offsets drifted; the real Locked/Large flags need rediscovery (--validate).
        public static readonly int OpeningDestroys = OffsetOverrides.Get("ChestComponent.OpeningDestroys", 0x20);  // ⚠ INVALID — pointer-field garbage; do not use
        public static readonly int Large           = OffsetOverrides.Get("ChestComponent.Large", 0x21);  // ⚠ INVALID — pointer-field garbage; do not use
        public static readonly int Locked          = OffsetOverrides.Get("ChestComponent.Locked", 0x25);  // ⚠ INVALID — pointer-field garbage; do not use
    }

    /// <summary>Monster component (name confirmed live: "Monster"). ⚠ The fork's IsBoss did NOT
    /// validate: a Unique boss ("Mighty Silverfist", QuadrillaBoss) still read 0 at +0x27 because the
    /// byte is the high byte of a pointer at +0x20 (2026-06-03). Use Rarity == Unique (✓ validated) to
    /// flag bosses/uniques instead — IsBoss here is both wrong and redundant.</summary>
    public static class MonsterComponent
    {
        public static readonly int IsBoss = OffsetOverrides.Get("MonsterComponent.IsBoss", 0x27); // ⚠ INVALID — pointer high-byte, 0 even for a Unique boss; use Rarity
    }

    /// <summary>Targetable component (name confirmed live: "Targetable"). ⚠ The fork's field offsets
    /// did NOT validate: +0x18 read a constant 144 (0x90) across every monster (2026-06-03), so it is
    /// NOT the IsTargetable bool. Offsets need rediscovery.</summary>
    public static class Targetable
    {
        public static readonly int Attackable   = OffsetOverrides.Get("Targetable.Attackable", 0x17); // ⚠ unconfirmed (read 0); likely wrong
        public static readonly int IsTargetable = OffsetOverrides.Get("Targetable.IsTargetable", 0x18); // ⚠ INVALID — read constant 144, not a bool; rediscover
    }

    /// <summary>Pathfinding component (name confirmed live: "Pathfinding"). BaseSpeed PLAUSIBLE —
    /// read varying values ~1183–1338 across monsters (2026-06-03), looks like a real per-monster int,
    /// but the "speed / 0 ⇒ immobile" semantics are unconfirmed. Flying suspect (read 4/5, not a bool).</summary>
    public static class PathfindingComponent
    {
        public static readonly int BaseSpeed = OffsetOverrides.Get("PathfindingComponent.BaseSpeed", 0xEC); // ✗ int — plausible (varies per monster); semantics unconfirmed
        public static readonly int Flying    = OffsetOverrides.Get("PathfindingComponent.Flying", 0xE5); // ⚠ suspect — read 4/5, not a clean bool
    }

    /// <summary>AreaTransition component. ✗ IDA-sourced, NOT yet validated (no transitions in the
    /// validation sample). Validate via <c>--validate</c> near a zone exit before use.</summary>
    public static class AreaTransitionComponent
    {
        public static readonly int GracePeriod   = OffsetOverrides.Get("AreaTransitionComponent.GracePeriod", 0x18); // ✗ float — unvalidated
        public static readonly int TeleportDelay = OffsetOverrides.Get("AreaTransitionComponent.TeleportDelay", 0x1C); // ✗ float — unvalidated
    }

    /// <summary>Positioned component.</summary>
    public static class Positioned
    {
        // ✓ validated live: player (friendly) = 0x01, hostile MastodonBoss = 0x00.
        // GameHelper2 rule: IsFriendly = (Reaction & 0x7F) == 1.
        public static readonly int Reaction = OffsetOverrides.Get("Positioned.Reaction", 0x1E0);

        // ✓ validated live (presence buff on/off sweep, Research --presence): the presence
        // area-of-effect scalar. Float, defaults to 1.0; a "+20% Presence AoE" buff drove it to
        // 1.0 from a ~0.92 base (≈ √1.2 radius scaling), and it tracked the buff on→off→on with
        // nothing else moving. Effective presence radius = base radius × this scalar.
        public static readonly int PresenceAoeScale = OffsetOverrides.Get("Positioned.PresenceAoeScale", 0x2A0);
    }

    /// <summary>
    /// TerrainStruct (base at AreaInstance+0x8A0). Validated live: TotalTiles (54,48) → 2592 tiles
    /// (matches TileDetails count); walkable grid 685584 bytes; BytesPerRow 621 → cellsPerRow 1242;
    /// grid 1242×1104 = (54×23)×(48×23). PoE2 has FOUR grid layers (0xD0/0xE8/0x100/0x118), so
    /// BytesPerRow sits at 0x130 — not GH2's 0x100.
    /// </summary>
    public static class Terrain
    {
        public static readonly int TotalTiles        = OffsetOverrides.Get("Terrain.TotalTiles", 0x18);  // ✓ StdTuple2D<long> (tilesX, tilesY)
        public static readonly int TileDetailsPtr    = OffsetOverrides.Get("Terrain.TileDetailsPtr", 0x28);  // ✓ StdVector of TileStructure (0x38 bytes)
        public static readonly int GridWalkableData  = OffsetOverrides.Get("Terrain.GridWalkableData", 0xD0);  // ✓ StdVector — packed walkable grid bytes
        public static readonly int GridLandscapeData = OffsetOverrides.Get("Terrain.GridLandscapeData", 0xE8);  // ✓ StdVector
        public static readonly int GridLayer3        = OffsetOverrides.Get("Terrain.GridLayer3", 0x100); // ✓ StdVector (extra PoE2 layer)
        public static readonly int GridLayer4        = OffsetOverrides.Get("Terrain.GridLayer4", 0x118); // ✓ StdVector (extra PoE2 layer)
        public static readonly int BytesPerRow       = OffsetOverrides.Get("Terrain.BytesPerRow", 0x130); // ✓ int (621 live) — cellsPerRow = ×2
        public static readonly int TileGridCells     = OffsetOverrides.Get("Terrain.TileGridCells", 23);    // tile = 23×23 grid cells
    }

    /// <summary>One entry in Terrain.TileDetailsPtr (0x38 bytes). ✓ validated (TgtPath gives tile names).</summary>
    public static readonly int TileStructureSize = OffsetOverrides.Get("Poe2.TileStructureSize", 0x38);
    public static class TileStructure
    {
        public static readonly int SubTileDetailsPtr = OffsetOverrides.Get("TileStructure.SubTileDetailsPtr", 0x00); // pointer
        public static readonly int TgtFilePtr        = OffsetOverrides.Get("TileStructure.TgtFilePtr", 0x08); // ✓ → TgtFileStruct
        public static readonly int TileHeight        = OffsetOverrides.Get("TileStructure.TileHeight", 0x30); // short
        public static readonly int RotationSelector  = OffsetOverrides.Get("TileStructure.RotationSelector", 0x36); // byte
    }

    public static class TgtFileStruct
    {
        public static readonly int TgtPath = OffsetOverrides.Get("TgtFileStruct.TgtPath", 0x08); // ✓ StdWString — full tile .tdt path (e.g. .../Feature/arena_01.tdt)
    }

    // ── Map UI — GH2, not yet live-checked ──
    public static class ImportantUi
    {
        public static readonly int MapParentPtr = OffsetOverrides.Get("ImportantUi.MapParentPtr", 0x738); // (GH2) from UiRoot/GameUi
    }

    public static class MapParent
    {
        public static readonly int LargeMapPtr = OffsetOverrides.Get("MapParent.LargeMapPtr", 0x50); // (GH2)
        public static readonly int MiniMapPtr  = OffsetOverrides.Get("MapParent.MiniMapPtr", 0x58); // (GH2)
    }

    /// <summary>
    /// MapUiElement (large map + minimap share this class/vtable). ✓ validated live: exactly two
    /// elements carry DefaultShift=(0,-20) with Zoom=0.5. Struct shape matches GH2 (shifted +0x70):
    /// Shift→DefaultShift = 8, DefaultShift→Zoom = 0x38.
    /// </summary>
    public static class MapUiElement
    {
        // 2026-09-04 patch shifted this block -0x18 (was 0x368/0x370/0x3A8). The struct SHAPE is
        // unchanged: Shift→DefaultShift = 8, DefaultShift→Zoom = 0x38. Re-found live via
        // --find-map (exactly two elements carry DefaultShift=(0,-20) with Zoom=0.5).
        public static readonly int Shift        = OffsetOverrides.Get("MapUiElement.Shift", 0x350); // ✓ StdTuple2D<float>
        public static readonly int DefaultShift = OffsetOverrides.Get("MapUiElement.DefaultShift", 0x358); // ✓ StdTuple2D<float> (0,-20)
        public static readonly int Zoom         = OffsetOverrides.Get("MapUiElement.Zoom", 0x390); // ✓ float (0.5 live)
    }

    /// <summary>UiElement base — ✓ validated live (GH2's offsets drifted: Self 0x30→0x8, Flags 0x1B8→0x180).
    /// Parent/Position/Size from the 2026-06-07 community offset dump (resources/additional offsets.txt);
    /// Position + Size confirmed live on the atlas-node class (size = 40×40 icons, positions vary per node).</summary>
    public static class UiElement
    {
        public static readonly int Self           = OffsetOverrides.Get("UiElement.Self", 0x08);  // ✓ self pointer
        public static readonly int Children       = OffsetOverrides.Get("UiElement.Children", 0x10);  // ✓ StdVector begin (child UiElement ptrs); End @ +0x18
        public static readonly int ChildrenEnd    = OffsetOverrides.Get("UiElement.ChildrenEnd", 0x18);  // ✓ StdVector end
        public static readonly int PositionModifier = OffsetOverrides.Get("UiElement.PositionModifier", 0xF0); // StdTuple2D<float>; added to parent pos when Flags bit 0x0A set (GH2 UiElementBase)
        public static readonly int Parent         = OffsetOverrides.Get("UiElement.Parent", 0xB8);  // (community) parent UiElement; true UI root = *(UiRoot+0xB8)
        public static readonly int RelativePos    = OffsetOverrides.Get("UiElement.RelativePos", 0x118); // ✓ StdTuple2D<float> position relative to parent (varies per atlas node)
        public static readonly int LocalScaleMul  = OffsetOverrides.Get("UiElement.LocalScaleMul", 0x130); // float local scale multiplier (also the atlas zoom on node elements)
        public static readonly int Flags          = OffsetOverrides.Get("UiElement.Flags", 0x168); // ✓ uint; IsVisibleLocal = bit 0x0B (toggle-diff: 0x2EF1↔0x26F1). 2026-09-04 patch shifted -0x18 (was 0x180); re-found live via --areascan (root 0x502EF0 / child 0x502EF1).
        public static readonly int FlagVisibleBit = OffsetOverrides.Get("UiElement.FlagVisibleBit", 0x0B);  // ✓ visible bit (set when shown)
        public static readonly int FlagModifyPosBit = OffsetOverrides.Get("UiElement.FlagModifyPosBit", 0x0A); // when set, PositionModifier (+0xF0) is added to the parent pos
        public static readonly int ScaleIndex     = OffsetOverrides.Get("UiElement.ScaleIndex", 0x18A); // byte; selects which axis scale(s) apply (1=v1,2=v2,3=v1×v2). root=3
        public static readonly int Text           = OffsetOverrides.Get("UiElement.Text", 0x360); // std::wstring of the element's displayed text (font name @ +0xC8).
                                                  // Validated live 2026-06-14: every text element (loot tags, skill
                                                  // rows, runeforge rows) holds its UTF-16 string here.
                                                  // 2026-09-04 patch shifted -0x30 (was 0x390) — NOT the -0x18 that
                                                  // Flags/MapUiElement moved, so don't extrapolate. Confirmed by
                                                  // content: +0x360 reads UI copy ("Entering Ogham Manor", "kills"),
                                                  // while +0x378 holds internal element NAMES ("modal_dialog_overlay",
                                                  // "HUD") and would silently pass a "is it a string?" check.
        public static readonly int SizeW          = OffsetOverrides.Get("UiElement.SizeW", 0x288); // ✓ float unscaled width  (atlas node = 40)
        public static readonly int SizeH          = OffsetOverrides.Get("UiElement.SizeH", 0x28C); // ✓ float unscaled height (atlas node = 40)
        // Full visibility is hierarchical: an element is shown iff its own bit 0x0B AND every
        // ancestor's bit are set. Walk Parent (+0xB8) up to the root.
        // Screen geometry (GH2 UiElementBaseFuncs): v1 = winW/2560, v2 = winH/1600 (BaseResolution
        // 2560×1600). ScaleValue(ScaleIndex, LocalScaleMul): idx1→(v1,v1) idx2→(v2,v2) idx3→(v1,v2),
        // else (mul,mul). screenPos = unscaledParentChainPos × ScaleValue; screenSize = UnscaledSize × ScaleValue.
        public const double BaseResW = 2560.0;
        public const double BaseResH = 1600.0;
    }

    /// <summary>"Runeshape Combinations" reward panel (rune-crafting league mechanic). The panel is found
    /// by a UI-FLAGS-FINGERPRINT walk with backtracking from GameUi (= <see cref="InGameState.UiRoot"/>,
    /// the UiRootStruct the game treats as a UiElement) — child indices drift per patch/restart, the Flags
    /// "role" bits don't. Each fingerprint is matched with the visible bit (0x800) masked out; step 0
    /// (window-container) must be VISIBLE = panel open. Validated live 2026-06-14 (Research --runeforge);
    /// re-validate per patch (the probe prints GameUi child flags on resolve-fail for re-fingerprinting).</summary>
    public static class Runeforge
    {
        // window-container (gate) → … → recipes-container. (visible bit masked out before compare.)
        public static readonly uint[] PanelFlagFingerprints =
            { 0x00462EF1, 0x00502EF3, 0x00502EF7, 0x00542EF1, 0x00502EF1 };
        public static readonly int GateStep = OffsetOverrides.Get("Runeforge.GateStep", 0);       // the window-container; its visible bit gates panel-open
        public static readonly int ViewportStep = OffsetOverrides.Get("Runeforge.ViewportStep", 2);   // this hop's element holds the scroll offset (+0x120)
        public static readonly int ScrollOffset = OffsetOverrides.Get("Runeforge.ScrollOffset", 0x120); // StdTuple2D<float> viewport scroll offset
        public static readonly int NameWString = OffsetOverrides.Get("Runeforge.NameWString", 0x390);  // visible row's kid[0]: inline std::wstring "<count>x <name>"
    }

    /// <summary>The world Entity currently under the cursor — monsters, NPCs, doodads, ground items
    /// included; <c>0</c> when nothing (or only UI) is hovered. A 3-hop pointer chain off InGameState
    /// (community, documented for v0.5.4): <c>host = *(InGameState + <see cref="HostFromInGameState"/>)</c>
    /// → <c>sub = *(host + <see cref="SubFromHost"/>)</c> → <c>entity = *(sub + <see cref="EntityFromSub"/>)</c>.
    /// Verified live 2026-06-29 (Research <c>--mouseover --hunt</c>: of 504 candidate offset combos this was
    /// the ONLY one whose resolved entity tracks the cursor) — still correct post-2026-06-25 patch (no shift).
    /// Cheap ground-truth for "what is the user pointing at", invaluable for mapping entities/elements.</summary>
    public static class MouseOver
    {
        // 2026-09-04 patch shifted +0x10 (was 0x300) — the old value now collides with UiRoot, so
        // the chain silently dead-ended on a UiElement instead of the hover host. Re-derived live
        // via Research --mouseover --hunt (resolved the hovered NPC). sub/ent hops unchanged.
        public static readonly int HostFromInGameState = OffsetOverrides.Get("MouseOver.HostFromInGameState", 0x310); // ✓ → host object
        public static readonly int SubFromHost         = OffsetOverrides.Get("MouseOver.SubFromHost", 0x3F0); // ✓ → sub object
        public static readonly int EntityFromSub       = OffsetOverrides.Get("MouseOver.EntityFromSub", 0xA8);  // ✓ → hovered Entity (0 when nothing/UI hovered)
    }

    /// <summary>The Currency Exchange panel (Kalguur market). The panel is a UI element — a direct child of
    /// GameUi (<c>InGameState+0x2F0</c>) — holding TWO inline <c>std::vector</c> headers (begin/end/cap) that
    /// each point to a heap array of stock entries. Cross-referenced 1:1 from the PoE1 ExileApi
    /// <c>CurrencyExchangePanel</c> (PoE1 offsets 0x430/0x448; PoE2 shifted to 0x478/0x490) and validated live
    /// 2026-06-29 (Research <c>--exchange-panel3</c>): Exalted-want/Chaos-have @ 50:1 → offered[0] Get=50 Give=1.
    /// The panel is resolved STRUCTURALLY (scan GameUi's visible children for one with valid stock vectors at
    /// both offsets) rather than by index — self-healing across patches. Stock vectors update their begin/end
    /// in place as orders fill, so read them LIVE. See <see cref="Poe2CurrencyExchange"/>.</summary>
    public static class CurrencyExchange
    {
        public static readonly int WantedStockVec  = OffsetOverrides.Get("CurrencyExchange.WantedStockVec", 0x478); // ✓ StdVector<StockEntry> — the "I Want" side
        public static readonly int OfferedStockVec = OffsetOverrides.Get("CurrencyExchange.OfferedStockVec", 0x490); // ✓ StdVector<StockEntry> — the "I Have" / offered side
        // StockEntry (stride 0x10): the ratio is Get/Give (derived, not stored). Last entry {0,0,n} = "< rest".
        public static readonly int EntryStride      = OffsetOverrides.Get("CurrencyExchange.EntryStride", 0x10);
        public static readonly int EntryGet         = OffsetOverrides.Get("CurrencyExchange.EntryGet", 0x0); // u16 — amount received
        public static readonly int EntryGive        = OffsetOverrides.Get("CurrencyExchange.EntryGive", 0x2); // u16 — amount given
        public static readonly int EntryListedCount = OffsetOverrides.Get("CurrencyExchange.EntryListedCount", 0x8); // i32 — listed stock at this order/bucket
        // The "I Have" quantity the user is selling = the Text of the panel's Nth direct child (the count
        // input). ✓ live 2026-06-29 (Research --exchange-qty --have 1689 → panel.Children[8].Text="1689").
        public static readonly int HaveQtyChildIndex = OffsetOverrides.Get("CurrencyExchange.HaveQtyChildIndex", 8);
    }

    /// <summary>The world-anchored "items on the ground" label layer (the <c>ItemsOnGroundLabelElement</c>
    /// in ExileApi terms). Its CHILDREN are the per-item loot tags (one text element per on-ground item);
    /// a tag's first line of <see cref="UiElement.Text"/> IS the item name (the PriceBook key). Located by
    /// the same FLAGS-FINGERPRINT walk-with-backtracking as <see cref="Runeforge"/> (child indices drift
    /// per patch, the Flags "role" bits don't): from GameUi (<c>InGameState+0x2F0</c>) descend
    /// child{<see cref="ContainerFlagFingerprints"/>[0]} → child[0]{[1]} → child[0]{[2]} = the container.
    /// The visible bit (0x800) is masked on both sides of every compare. Confining the loot-tag scan to
    /// THIS subtree (instead of the whole UI tree) is what stops value chips landing on unrelated UI panels
    /// whose text happens to match a priced item name. Validated live 2026-06-29 (Research <c>--lootstruct</c>
    /// / <c>--lootmap</c>); re-fingerprint per patch if it stops resolving.</summary>
    public static class GroundLabels
    {
        // GameUi → [overlay layer] → [sub-layer] → [labels container]. (visible bit masked before compare.)
        public static readonly uint[] ContainerFlagFingerprints =
            { 0x00542EF3, 0x00402EF3, 0x00502EF1 };
    }

    /// <summary>Ritual tribute-shop reward grid. The reward TILES are item-slot UiElements (same "ItemFrame"
    /// element type as the flask bar): each holds its reward item Entity at <see cref="TileSlotItem"/>. The
    /// grid is found by walking up from a shop-signature text element to the ancestor whose child is a
    /// container of these tiles (see <c>Poe2Live.ReadRitualRewards</c>). Validated live 2026-06-20 (Research
    /// <c>--tooltip-capture</c>): all 5 offered rewards read as full item entities with no hover needed.</summary>
    public static class Ritual
    {
        public static readonly int TileSlotItem = OffsetOverrides.Get("Ritual.TileSlotItem", 0x4F8); // ✓ item-slot UiElement → reward item Entity (also the flask-bar slot field)
    }

    /// <summary>Atlas map-node UiElement (a subclass with its own vtable; ~1200+ instances live in the
    /// open Atlas). Fields from the 2026-06-07 community dump; structurally confirmed live: biome
    /// (+0x32E) spread 0..12, per-node positions (UiElement.RelativePos), 40×40 size, scale (+0x130) =
    /// the atlas zoom. (+0x300 is a map-TYPE id shared by same-type nodes — NOT unique per node.)
    ///
    /// <para><b>PROJECTION (✓ live, pan + zoom):</b> a node's on-screen position is
    /// <c>screen = (UIscale × zoom) × relPos + offset</c>, where relPos = +0x118 (read live; the game
    /// rewrites it on PAN so pan is free), zoom = +0x130 (read live; ~0.85 max zoom-out → larger zoomed
    /// in), UIscale = winH/1600, offset ≈ factor×½icon ≈ (15,13) @ 1080p/zoom-0.85. NOT a perspective
    /// homography. The overlay derives the WHOLE projection live from the window height + live zoom
    /// (RadarApp.AtlasProjection) — resolution-correct with no calibration. <b>Recovery after a patch:</b> run
    /// <c>POE2Radar.Research --atlas-probe</c> (Atlas map open) — it re-locates the class + canvas,
    /// validates every offset, and prints the derived projection. Only the node-class vtable drifts.
    /// See resources/atlas-research-notes.md "FULLY SOLVED".</para></summary>
    /// <summary>The EndgameMaps row a node points at (node <see cref="AtlasNode.MapNodeId"/> +0x300 → row).
    /// Its +0x00 → the WorldAreas row, whose +0x00 is the Id ("MapXxx") and +0x08 is the LOCALIZED display
    /// name ("Savannah"/"Digsite"/"Precursor Tower"). ✓ validated live 2026-06-16 (Research --atlas-mapname);
    /// reading +0x08 fixed web-UI filters where Prettify(code) mismatched the in-game name.</summary>
    public static class AtlasMapRow
    {
        public static readonly int WorldAreaName = OffsetOverrides.Get("AtlasMapRow.WorldAreaName", 0x08); // ✓ WorldAreas row +0x08 → UTF-16 localized map name
    }

    public static class AtlasNode
    {
        public static readonly int MapNodeId   = OffsetOverrides.Get("AtlasNode.MapNodeId", 0x300); // ✓ u32 — distinct per node
        public static readonly int Content     = OffsetOverrides.Get("AtlasNode.Content", 0x310); // (community) u32 content (0 = none)
        public static readonly int State       = OffsetOverrides.Get("AtlasNode.State", 0x32C); // (community) u8 state (seen =1 on loaded nodes)
        public static readonly int Biome       = OffsetOverrides.Get("AtlasNode.Biome", 0x32E); // ✓ u8 biome index (0..12)
        public static readonly int Flags       = OffsetOverrides.Get("AtlasNode.Flags", 0x32F); // (community) u8: bit0 unlocked, bit1 visited
        public static readonly int GridPos     = OffsetOverrides.Get("AtlasNode.GridPos", 0x320); // ✓ live 2026-06-08 — StdTuple2D<int> atlas grid coord (X,Y); 1:1 with node, range small (e.g. X[-16..31] Y[0..47]). The key for node-graph pathfinding. (GameHelper2-sourced)
        public static readonly int Completion  = OffsetOverrides.Get("AtlasNode.Completion", 0x339); // (community) u8 per-node completion id
        public static readonly int ContentVec  = OffsetOverrides.Get("AtlasNode.ContentVec", 0x350); // (community) StdVector begin (content list); End @ +0x358

        /// <summary>Alternate node-DATA model (GameHelper2): <c>*(*(node+0x10)+0x20)</c> → a struct with
        /// biome <c>+0x2CE</c> / status byte <c>+0x2CF</c> (bit0 accessible, bit1 completed) / mapId at
        /// <c>+0x2A0</c> (ptr→ptr→ptr→UTF-16 "MapXxx"). Validated live 2026-06-08 (biome matches the
        /// element's own <see cref="Biome"/> 200/200). POE2Radar reads biome/mapId DIRECTLY off the
        /// element (<see cref="Biome"/>, <see cref="MapNodeId"/> + the +0x300 EndgameMaps row), so this
        /// deeper model is an alternate source, not required.</summary>
        public static readonly int DataStorage = OffsetOverrides.Get("AtlasNode.DataStorage", 0x10);   // *(node+0x10) → storage
        public static readonly int DataModel   = OffsetOverrides.Get("AtlasNode.DataModel", 0x20);   // *(storage+0x20) → nodeData
        public static readonly int DataBiome   = OffsetOverrides.Get("AtlasNode.DataBiome", 0x2CE);  // u8 within nodeData
        public static readonly int DataStatus  = OffsetOverrides.Get("AtlasNode.DataStatus", 0x2CF);  // u8 within nodeData: bit0 accessible, bit1 completed
        public static readonly int DataMapId   = OffsetOverrides.Get("AtlasNode.DataMapId", 0x2A0);  // ptr chain → UTF-16 "MapXxx"
    }

    /// <summary>Atlas CONNECTION GRAPH (✓ live 2026-06-08, GameHelper2-sourced). The node canvas (the
    /// parent holding the most node-class children — POE2Radar's detected <c>_nodeCanvas</c>) carries a
    /// <c>StdVector</c> of edges at <c>+0x5A8</c>. Each edge is 20 bytes: <c>{ int unknown; StdTuple2D&lt;int&gt;
    /// source; StdTuple2D&lt;int&gt; target }</c> — source @ +0x04, target @ +0x0C, both in node grid
    /// coords (<see cref="AtlasNode.GridPos"/>). Live: 291 edges, 100% endpoints on real grid positions,
    /// avg degree 2.9 / max 5 (a real sparse atlas graph). This is what enables "route from the player's
    /// current node to a target node in the fewest hops" (A* over the graph, per GH2's FindShortestPathAStar).
    /// Re-discover after a patch with <c>POE2Radar.Research --atlas-graph</c>.</summary>
    public static class AtlasGraph
    {
        public static readonly int ConnectionsVec = OffsetOverrides.Get("AtlasGraph.ConnectionsVec", 0x5A8); // on the node canvas: StdVector<edge> begin; End @ +0x5B0
        public static readonly int EdgeStride     = OffsetOverrides.Get("AtlasGraph.EdgeStride", 20);
        public static readonly int EdgeSourceOff  = OffsetOverrides.Get("AtlasGraph.EdgeSourceOff", 0x04);  // StdTuple2D<int>
        public static readonly int EdgeTargetOff  = OffsetOverrides.Get("AtlasGraph.EdgeTargetOff", 0x0C);  // StdTuple2D<int>

        /// <summary>Current-location ("player icon") marker: the SINGLE non-node UiElement in the atlas
        /// UI subtree whose <c>+0x300</c> field points at a node-class element. That target node is the map
        /// the player is currently in (✓ live 2026-06-08 — held even while standing in a hideout). The
        /// accessor is structural, not vtable-keyed (the marker's class drifts per patch), so it's found by
        /// "the lone non-node element whose +0x300 ∈ node set". <c>currentNode = *(marker + 0x300)</c>, then
        /// read the node's <see cref="AtlasNode.GridPos"/>. Re-discover with <c>--atlas-marker</c>.</summary>
        public static readonly int CurrentMarkerNodePtr = OffsetOverrides.Get("AtlasGraph.CurrentMarkerNodePtr", 0x300);
    }

    /// <summary>Atlas screen panel — a PERSISTENT direct child of UiRoot (the element at
    /// <c>InGameState+0x2F0</c>, walked via its Children StdVector <c>+0x10</c>) at <see cref="UiRootChildIndex"/>.
    /// Present from a cold launch even when the atlas has NEVER been opened (✓ live 2026-06-08); its
    /// UiElement visible bit (Flags <c>+0x180</c> bit <c>0x0B</c>) is the only thing that toggles when the
    /// atlas opens/closes (closed flags 0x5626F5 → open 0x562EF5). This is the cheap atlas open-gate:
    /// reading this one element's visible bit is ~4 reads, versus BFS-walking the ~50k-element UI tree to
    /// (re)detect the node class — which while the atlas is closed can never succeed and so would burn that
    /// BFS every retry. <b>If a patch shifts UiRoot's children this index drifts</b> — re-discover by
    /// diffing the DevTree <c>/api/ui-flat</c> tree closed-vs-open (the element whose visible bit flips at
    /// the shallowest stable path). <see cref="ExpectedChildCount"/> is a secondary signature (18 children).</summary>
    public static class AtlasPanel
    {
        public static readonly int UiRootChildIndex  = OffsetOverrides.Get("AtlasPanel.UiRootChildIndex", 22); // ✓ live 2026-06-08 — stable across a cold restart
        public static readonly int ExpectedChildCount = OffsetOverrides.Get("AtlasPanel.ExpectedChildCount", 18); // ✓ signature (panel had 18 children closed + open)
    }

    /// <summary>World hover tracker (community, 2026-06-07): <c>*(UiRoot+0x7D8)+0x630</c>; hovered entity
    /// at +0x18. Singletons share vtable (image+0x2D707D8). The capture anchor for "what am I pointing at".</summary>
    public static class HoverTracker
    {
        public static readonly int FromUiRoot   = OffsetOverrides.Get("HoverTracker.FromUiRoot", 0x7D8); // *(UiRoot + 0x7D8) → tracker container
        public static readonly int WorldTracker = OffsetOverrides.Get("HoverTracker.WorldTracker", 0x630); // + 0x630 → world hover tracker
        public static readonly int HoveredEntity = OffsetOverrides.Get("HoverTracker.HoveredEntity", 0x18); // + 0x18 → hovered entity/element
    }

    /// <summary>The passive skill tree screen — a persistent direct child of UiRoot whose visible bit is
    /// SET while the tree is open, exactly like <see cref="AtlasPanel"/>. Used to suppress the radar
    /// while the tree covers the world.
    ///
    /// <para><b>Polarity (validated 2026-09-04, one state per measurement):</b> tree CLOSED → hidden,
    /// tree OPEN → visible. This is a PANEL, not a HUD layer. Reading it the other way round (treating
    /// "hidden" as "a panel is covering the world") inverts the gate: the radar is then suppressed
    /// during normal play and drawn on top of the tree — the exact symptom that behaviour produced.</para>
    ///
    /// <para><b>Why not hierarchical visibility on the map element.</b> Measured with Research
    /// <c>--mapdiag --secs</c>: toggling the tree produces ZERO change to the map elements' own or
    /// ancestor visible bits — the map UI and this panel are SIBLINGS, so no parent walk can see it.
    /// The ground-label layer (UiRoot child[8]) is likewise unaffected.</para>
    ///
    /// <para><b>Identification.</b> <see cref="Fingerprint"/> alone is NOT unique — 5 of 124 UiRoot
    /// children share it (22/24/25/29/64), because they are all screen panels; 22 is the
    /// <see cref="AtlasPanel"/>. Their child counts are 18/8/9/10/13, so
    /// (fingerprint, <see cref="ExpectedChildCount"/>) is a unique key and the gate self-heals when the
    /// index drifts. <see cref="UiRootChildIndex"/> is only a fast path. Re-derive after a patch with
    /// Research <c>--uitoggle</c>, and confirm polarity with <c>--huddiag</c> in each state separately —
    /// never from a single toggle-while-watching run, which cannot tell you which state was which.</para></summary>
    public static class PassiveTreePanel
    {
        public static readonly int UiRootChildIndex   = OffsetOverrides.Get("PassiveTreePanel.UiRootChildIndex", 24);   // fast path; validated before use
        public static readonly int ExpectedChildCount = OffsetOverrides.Get("PassiveTreePanel.ExpectedChildCount", 8);  // disambiguates from the other panels
        public static readonly uint Fingerprint       = OffsetOverrides.Get("PassiveTreePanel.Fingerprint", 0x005626F5u); // Flags with the visible bit masked out
    }
}
