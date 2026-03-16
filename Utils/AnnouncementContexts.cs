namespace FFIV_ScreenReader.Utils
{
    /// <summary>
    /// Centralized announcement deduplication context strings.
    /// Each context represents a distinct UI element or state that tracks
    /// what was last announced to avoid repeating the same text.
    /// </summary>
    public static class AnnouncementContexts
    {
        // Battle commands/selection
        public const string BATTLE_COMMAND_SELECT = "BattleCommand.Select";
        public const string BATTLE_ITEM_SELECT = "BattleItem.Select";
        public const string BATTLE_ABILITY_SELECT = "BattleAbility.Select";

        // Battle messages/actions
        public const string BATTLE_ACTION = "BattleAction";
        public const string BATTLE_CONDITION_ADD = "Battle.ConditionAdd";
        public const string BATTLE_SET_COMMAND_MESSAGE = "Battle.SetCommandMessage";
        public const string BATTLE_TURN = "Battle.Turn";
        public const string BATTLE_COMMAND_MESSAGE = "Battle.CommandMessage";
        public const string BATTLE_TARGET_PLAYER = "Battle.Target.Player";
        public const string BATTLE_TARGET_ENEMY = "Battle.Target.Enemy";

        // Ability menu
        public const string ABILITY_COMMAND = "AbilityMenu.Command";
        public const string ABILITY_CONTENT = "AbilityMenu.Content";
        public const string ABILITY_USE_TARGET = "AbilityMenu.UseTarget";

        // Item/Equipment menu
        public const string EQUIPMENT_ANNOUNCEMENT = "EquipmentMenu.Announcement";
        public const string ITEM_LIST = "ItemMenu.List";
        public const string EQUIPMENT_SELECT = "EquipmentMenu.Select";
        public const string ITEM_USE_TARGET = "ItemMenu.UseTarget";

        // Config menu
        public const string CONFIG_COMMAND = "ConfigMenu.Command";
        public const string CONFIG_KEYS_SETTING = "ConfigMenu.KeysSetting";
        public const string CONFIG_ARROW_VALUE = "ConfigMenu.ArrowValue";
        public const string CONFIG_SLIDER_VALUE = "ConfigMenu.SliderValue";
        public const string CONFIG_TOUCH_ARROW_VALUE = "ConfigMenu.TouchArrowValue";
        public const string CONFIG_TOUCH_SLIDER_VALUE = "ConfigMenu.TouchSliderValue";

        // Naming
        public const string NAMING_SELECT = "Naming.Select";

        // Party setting
        public const string PARTY_SETTING_SELECT = "PartySetting.Select";

        // Shop
        public const string SHOP_ITEM = "Shop.Item";

        // Popups
        public const string GAMEOVER_SELECT = "GameOverSelect";
        public const string GAMEOVER_LOAD = "GameOverLoad";
        public const string SAVE_LOAD_POPUP_BUTTON = "SaveLoadPopupButton";

        // Title menu
        public const string TITLE_MENU_COMMAND = "TitleMenu.Command";

        // Bestiary
        public const string BESTIARY_LIST_ENTRY = "Bestiary.ListEntry";
        public const string BESTIARY_DETAIL_STAT = "Bestiary.DetailStat";
        public const string BESTIARY_FORMATION = "Bestiary.Formation";
        public const string BESTIARY_MAP = "Bestiary.Map";
        public const string BESTIARY_STATE = "Bestiary.State";

        // Music player
        public const string MUSIC_LIST_ENTRY = "MusicPlayer.ListEntry";

        // Gallery
        public const string GALLERY_LIST_ENTRY = "Gallery.ListEntry";
    }
}
