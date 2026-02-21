using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine;

namespace FFIV_ScreenReader.Utils
{
    /// <summary>
    /// Translates Japanese entity names to English using an embedded dictionary.
    /// All translations are compiled directly into the DLL.
    /// Uses lazy initialization to avoid IL2CPP static initializer issues.
    /// </summary>
    public static class EntityTranslator
    {
        private static Dictionary<string, string> translations;
        private static bool isInitialized = false;

        // Matches numeric prefix (e.g., "6:") or SC prefix (e.g., "SC01:") at start of entity names
        private static readonly Regex EntityPrefixRegex = new Regex(
            @"^((?:SC)?\d+:)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches circled number suffix at END (① through ⑳)
        private static readonly Regex EntitySuffixRegex = new Regex(
            @"([①②③④⑤⑥⑦⑧⑨⑩⑪⑫⑬⑭⑮⑯⑰⑱⑲⑳])$",
            RegexOptions.Compiled);

        /// <summary>
        /// Initializes the translator with all embedded translations.
        /// Uses lazy initialization to avoid IL2CPP static initializer timing issues.
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;

            // Create dictionary with all 559 translations
            translations = new Dictionary<string, string>
            {
                { "城１", "Castle 1" },
                { "ミストドラゴン会話", "Mist Dragon Conversation" },
                { "男（青）④", "Man (Blue) 4" },
                { "テラ", "Tellah" },
                { "オクトマンモス", "Octomammoth" },
                { "兵士（紫）②", "Soldier (Purple) 2" },
                { "兵士（紫）①", "Soldier (Purple) 1" },
                { "アンナ死亡", "Anna Death" },
                { "アンナ", "Anna" },
                { "兵士（紫）", "Soldier (Purple)" },
                { "ババア（紫）", "Old Woman (Purple)" },
                { "新聞", "Newspaper" },
                { "学者（青）①", "Scholar (Blue) 1" },
                { "女性（紫）③", "Woman (Purple) 3" },
                { "ジジイ（赤）", "Old Man (Red)" },
                { "ジジイ（赤）②", "Old Man (Red) 2" },
                { "砂漠の高熱病には……", "For Desert Fever..." },
                { "ローザ", "Rosa" },
                { "ババア（青）①", "Old Woman (Blue) 1" },
                { "暖炉", "Fireplace" },
                { "ネミングウェイ（青）", "Namingway (Blue)" },
                { "リディア（子供）", "Rydia (Child)" },
                { "リディアの母", "Rydia's Mother" },
                { "ジジイ（青）", "Old Man (Blue)" },
                { "ドアが開く（手前）", "Door Opens (Front)" },
                { "兵士（赤）②", "Soldier (Red) 2" },
                { "兵士（赤）①", "Soldier (Red) 1" },
                { "コリジョン", "Collision" },
                { "スイッチ", "Switch" },
                { "白魔道士（赤）①", "White Mage (Red) 1" },
                { "白魔道士（赤）②", "White Mage (Red) 2" },
                { "白魔道士（赤）③", "White Mage (Red) 3" },
                { "黒魔道士（青）①", "Black Mage (Blue) 1" },
                { "黒魔道士（青）②", "Black Mage (Blue) 2" },
                { "黒魔道士（青）③", "Black Mage (Blue) 3" },
                { "女性（赤）", "Woman (Red)" },
                { "ババア（紫）⑤", "Old Woman (Purple) 5" },
                { "ヒゲオヤジ（赤）①", "Bearded Man (Red) 1" },
                { "隠し扉が開く", "Hidden Door Opens" },
                { "デビルロード", "Devil's Road" },
                { "浮力とその原理", "Buoyancy and Its Principles" },
                { "造船技術の歴史", "History of Shipbuilding" },
                { "世界の鳥　大図鑑", "Encyclopedia of World Birds" },
                { "バロン:ギサールの野菜", "Baron: Gysahl Greens" },
                { "チョコボ(黄)", "Chocobo (Yellow)" },
                { "チョコボ(白)", "Chocobo (White)" },
                { "ギサールの野菜用", "For Gysahl Greens" },
                { "ヒゲオヤジ（赤）", "Bearded Man (Red)" },
                { "ヒゲオヤジ（紫）", "Bearded Man (Purple)" },
                { "ギルバート", "Edward" },
                { "アントリオンの左足", "Antlion's Left Leg" },
                { "アントリオンの右足", "Antlion's Right Leg" },
                { "氷の壁", "Ice Wall" },
                { "リディアがファイアを覚える", "Rydia Learns Fire" },
                { "ヤンが仲間にR", "Yang Joins Party R" },
                { "ヤンが仲間にL", "Yang Joins Party L" },
                { "幻獣（青）", "Eidolon (Blue)" },
                { "ヤン", "Yang" },
                { "ファブール:ギサールの野菜", "Fabul: Gysahl Greens" },
                { "モンク僧（青）①", "Monk (Blue) 1" },
                { "モンク僧（青）②", "Monk (Blue) 2" },
                { "モンク僧（青）③", "Monk (Blue) 3" },
                { "モンク僧（青）④", "Monk (Blue) 4" },
                { "ファブール防衛戦", "Fabul Defense Battle" },
                { "王様（青）", "King (Blue)" },
                { "カイン", "Kain" },
                { "クリスタル青", "Crystal (Blue)" },
                { "男の子（青）①", "Boy (Blue) 1" },
                { "踊り子（水着姿　赤）", "Dancer (Swimsuit Red)" },
                { "男（青）③", "Man (Blue) 3" },
                { "女性（紫）②", "Woman (Purple) 2" },
                { "白魔道士（赤）", "White Mage (Red)" },
                { "ヤンの奥さん", "Yang's Wife" },
                { "ババア（青）", "Old Woman (Blue)" },
                { "ファブール防衛戦後", "After Fabul Defense" },
                { "技師（青）", "Engineer (Blue)" },
                { "セシル", "Cecil" },
                { "白魔道士（赤）⑦", "White Mage (Red) 7" },
                { "黒魔道士（青）⑧", "Black Mage (Blue) 8" },
                { "長老（紫）", "Elder (Purple)" },
                { "黒魔道士（青）", "Black Mage (Blue)" },
                { "ポスター", "Poster" },
                { "女性（水着姿　青）⑥", "Woman (Swimsuit Blue) 6" },
                { "黒魔道士（青）④", "Black Mage (Blue) 4" },
                { "ネミングウェイ（青）⑤", "Namingway (Blue) 5" },
                { "試練:ギサールの野菜", "Ordeals: Gysahl Greens" },
                { "試練の山入り口", "Mt. Ordeals Entrance" },
                { "ゴルベーザ", "Golbez" },
                { "ギロチン", "Guillotine" },
                { "テラと再会", "Reunion with Tellah" },
                { "パロム", "Palom" },
                { "ポロム", "Porom" },
                { "スカルミリョーネ1", "Scarmiglione 1" },
                { "セーブポイント", "Save Point" },
                { "幻獣（紫）", "Eidolon (Purple)" },
                { "セシル（パラディン）", "Cecil (Paladin)" },
                { "sc_e_0068発火用", "sc_e_0068 Ignition" },
                { "長老（青）①", "Elder (Blue) 1" },
                { "男（赤）", "Man (Red)" },
                { "ヒゲオヤジ（青）", "Bearded Man (Blue)" },
                { "回復", "Recovery" },
                { "カイナッツォ", "Cagnazzo" },
                { "シド", "Cid" },
                { "女性（赤）①", "Woman (Red) 1" },
                { "ジジイ（紫）②", "Old Man (Purple) 2" },
                { "ババア（青）⑤", "Old Woman (Blue) 5" },
                { "ヒゲオヤジ（赤）⑥", "Bearded Man (Red) 6" },
                { "寝る⑦", "Sleeping 7" },
                { "踊り子（水着姿　赤）③", "Dancer (Swimsuit Red) 3" },
                { "踊り子（水着　青）④", "Dancer (Swimsuit Blue) 4" },
                { "女の子（青）⑨", "Girl (Blue) 9" },
                { "ネミングウェイ（青）⑧", "Namingway (Blue) 8" },
                { "熱がる", "Feels Hot" },
                { "チョコボの生態……。", "Chocobo Ecology..." },
                { "あなたにも飼えるチョコボ……。", "You Can Raise a Chocobo Too..." },
                { "男性（青）", "Man (Blue)" },
                { "ジジイ（紫）④", "Old Man (Purple) 4" },
                { "ヒゲオヤジ（青）⑤", "Bearded Man (Blue) 5" },
                { "男性（紫）", "Man (Purple)" },
                { "踊り子（水着姿　青）②", "Dancer (Swimsuit Blue) 2" },
                { "男性（赤）⑥", "Man (Red) 6" },
                { "女性（紫）⑩", "Woman (Purple) 10" },
                { "男性（青）⑧", "Man (Blue) 8" },
                { "隠し扉", "Hidden Door" },
                { "踊り子（水着姿　赤）⑦", "Dancer (Swimsuit Red) 7" },
                { "ババア（紫）⑨", "Old Woman (Purple) 9" },
                { "男性（赤）①", "Man (Red) 1" },
                { "ババア（紫）②", "Old Woman (Purple) 2" },
                { "ヒゲオヤジ（青）④", "Bearded Man (Blue) 4" },
                { "女性（紫）⑤", "Woman (Purple) 5" },
                { "男性（青）⑨", "Man (Blue) 9" },
                { "ジジイ（赤）⑦", "Old Man (Red) 7" },
                { "踊り子（水着姿　青）⑧", "Dancer (Swimsuit Blue) 8" },
                { "女性（赤）⑥", "Woman (Red) 6" },
                { "ジジイ（紫）", "Old Man (Purple)" },
                { "扉が開く", "Door Opens" },
                { "踊り子（水着姿　青）", "Dancer (Swimsuit Blue)" },
                { "踊り子（水着姿　紫）", "Dancer (Swimsuit Purple)" },
                { "カエル（緑）⑩", "Frog (Green) 10" },
                { "カエル（緑）⑨", "Frog (Green) 9" },
                { "カエル（緑）⑥", "Frog (Green) 6" },
                { "兵士（赤）③", "Soldier (Red) 3" },
                { "兵士（赤）④", "Soldier (Red) 4" },
                { "カエル（緑）⑧", "Frog (Green) 8" },
                { "カエル（緑）⑪", "Frog (Green) 11" },
                { "カエル（緑）⑦", "Frog (Green) 7" },
                { "カエル（緑）⑤", "Frog (Green) 5" },
                { "カエル（緑）⑫", "Frog (Green) 12" },
                { "踊り子（水着姿　赤）①", "Dancer (Swimsuit Red) 1" },
                { "踊り子（水着姿　赤）②", "Dancer (Swimsuit Red) 2" },
                { "神官（紫）④", "Priest (Purple) 4" },
                { "神官（紫）③", "Priest (Purple) 3" },
                { "神官（紫）⑥", "Priest (Purple) 6" },
                { "神官（紫）⑤", "Priest (Purple) 5" },
                { "神官（紫）⑦", "Priest (Purple) 7" },
                { "神官（紫）⑧", "Priest (Purple) 8" },
                { "神官（紫）⑨", "Priest (Purple) 9" },
                { "神官（紫）⑩", "Priest (Purple) 10" },
                { "ギルバートと再会", "Reunion with Edward" },
                { "男性（赤）", "Man (Red)" },
                { "女性（青）", "Woman (Blue)" },
                { "ギルバートの竪琴だ…", "Edward's Harp..." },
                { "兵士（赤）", "Soldier (Red)" },
                { "ギサールの野菜用（右）", "For Gysahl Greens (Right)" },
                { "ギサールの野菜用（左）", "For Gysahl Greens (Left)" },
                { "ギサールの野菜", "Gysahl Greens" },
                { "チョコボ(黒)", "Chocobo (Black)" },
                { "カエル(緑)", "Frog (Green)" },
                { "小人(赤)", "Mini (Red)" },
                { "豚", "Pig" },
                { "ミスリルナイフ", "Mythril Knife" },
                { "小人(青)", "Mini (Blue)" },
                { "武器屋(カエル(緑))", "Weapon Shop (Frog Green)" },
                { "ネミングウェイ(青)", "Namingway (Blue)" },
                { "孤島:ギサールの野菜", "Island: Gysahl Greens" },
                { "ジジイ（紫）※防具屋", "Old Man (Purple) *Armor Shop" },
                { "アガルトの村の井戸でマグマの石をつかう", "Use Magma Stone at Agart Village Well" },
                { "ヒゲオヤジ（青）①", "Bearded Man (Blue) 1" },
                { "女性（赤）④", "Woman (Red) 4" },
                { "ヒゲオヤジ（赤）※道具屋③", "Bearded Man (Red) *Item Shop 3" },
                { "ジジイ（紫）⑧", "Old Man (Purple) 8" },
                { "女の子（赤）⑥", "Girl (Red) 6" },
                { "男の子（青）⑦", "Boy (Blue) 7" },
                { "ヒゲオヤジ（青）※宿屋⑤", "Bearded Man (Blue) *Inn 5" },
                { "ババア（赤）⑨", "Old Woman (Red) 9" },
                { "男（赤）②", "Man (Red) 2" },
                { "男（青）※武器屋", "Man (Blue) *Weapon Shop" },
                { "ババア（青）②", "Old Woman (Blue) 2" },
                { "男の子（赤）⑥", "Boy (Red) 6" },
                { "ジジイ（紫）⑦", "Old Man (Purple) 7" },
                { "ヒゲオヤジ（紫）④", "Bearded Man (Purple) 4" },
                { "男（青）①", "Man (Blue) 1" },
                { "女の子（青）⑤", "Girl (Blue) 5" },
                { "学者（青）", "Scholar (Blue)" },
                { "望遠鏡を覗く", "Look Through Telescope" },
                { "学者（紫）", "Scholar (Purple)" },
                { "金属が重い", "Metal is Heavy" },
                { "ダークエルフ", "Dark Elf" },
                { "ワールドマップ戻る用", "Return to World Map" },
                { "メーガス三姉妹", "Magus Sisters" },
                { "ドグ", "Dog" },
                { "マグ", "Mag" },
                { "ラグ", "Rag" },
                { "テラ死亡", "Tellah Death" },
                { "鍵がかかって通れない", "Locked Cannot Pass" },
                { "カイン（仮）", "Kain (Temp)" },
                { "亡きバロン王との会話", "Conversation with Late King Baron" },
                { "王様（赤）", "King (Red)" },
                { "ワールドマップ（地底）城", "World Map (Underground) Castle" },
                { "飛空艇（エンタープライズ号）", "Airship (Enterprise)" },
                { "ドワーフ兵（青）①", "Dwarf Soldier (Blue) 1" },
                { "ドワーフ兵（青）②", "Dwarf Soldier (Blue) 2" },
                { "ドワーフ兵（青）③", "Dwarf Soldier (Blue) 3" },
                { "ドワーフ兵（青）④", "Dwarf Soldier (Blue) 4" },
                { "ドワーフ兵（青）⑤", "Dwarf Soldier (Blue) 5" },
                { "ドワーフ兵（青）⑥", "Dwarf Soldier (Blue) 6" },
                { "ドワーフ兵（青）⑦", "Dwarf Soldier (Blue) 7" },
                { "ドワーフ兵（青）⑧", "Dwarf Soldier (Blue) 8" },
                { "ルカ", "Luca" },
                { "ドワーフ兵（青）", "Dwarf Soldier (Blue)" },
                { "ドワーフのクリスタル強奪", "Dwarf Crystal Theft" },
                { "ドワーフ王①", "Dwarf King 1" },
                { "人形", "Doll" },
                { "ゴルベーザの手", "Golbez's Hand" },
                { "リディア", "Rydia" },
                { "ドワーフ兵（青）※宿屋", "Dwarf Soldier (Blue) *Inn" },
                { "ドワーフ兵（青）※道具屋", "Dwarf Soldier (Blue) *Item Shop" },
                { "ドワーフ兵（青）※防具屋", "Dwarf Soldier (Blue) *Armor Shop" },
                { "ドワーフ兵（青）※武器屋", "Dwarf Soldier (Blue) *Weapon Shop" },
                { "開発室メッセージ", "Developer Room Message" },
                { "ドワーフ兵（赤）②", "Dwarf Soldier (Red) 2" },
                { "ドワーフ兵（赤）①", "Dwarf Soldier (Red) 1" },
                { "HP・MP回復", "HP/MP Recovery" },
                { "ドワーフ兵（紫）①", "Dwarf Soldier (Purple) 1" },
                { "ドワーフ兵（紫）③", "Dwarf Soldier (Purple) 3" },
                { "ドワーフ兵（紫）②", "Dwarf Soldier (Purple) 2" },
                { "ドワーフ兵（紫）④", "Dwarf Soldier (Purple) 4" },
                { "ドワーフ兵（紫）⑥", "Dwarf Soldier (Purple) 6" },
                { "ドワーフ兵（紫）⑤", "Dwarf Soldier (Purple) 5" },
                { "ドワーフ兵（紫）※道具屋", "Dwarf Soldier (Purple) *Item Shop" },
                { "ドワーフ兵（紫）※宿屋", "Dwarf Soldier (Purple) *Inn" },
                { "ドワーフ兵（紫）", "Dwarf Soldier (Purple)" },
                { "ドワーフ兵（紫）※武器屋", "Dwarf Soldier (Purple) *Weapon Shop" },
                { "ドワーフ兵（紫）※防具屋", "Dwarf Soldier (Purple) *Armor Shop" },
                { "カギのかかっている扉", "Locked Door" },
                { "ルゲイエ博士", "Dr. Lugae" },
                { "ワープ装置", "Warp Device" },
                { "ルゲイエ博士とバトル", "Battle with Dr. Lugae" },
                { "ルビカンテ", "Rubicante" },
                { "幻獣（赤）", "Eidolon (Red)" },
                { "大砲室爆破", "Cannon Room Explosion" },
                { "ひじょうぐち", "Emergency Exit" },
                { "シド自爆", "Cid Self-Destruct" },
                { "扉を開く", "Open Door" },
                { "兵士（青）②", "Soldier (Blue) 2" },
                { "兵士（青）③", "Soldier (Blue) 3" },
                { "兵士（青）④", "Soldier (Blue) 4" },
                { "兵士（青）⑤", "Soldier (Blue) 5" },
                { "兵士（青）⑥", "Soldier (Blue) 6" },
                { "兵士（青）⑦", "Soldier (Blue) 7" },
                { "兵士（青）①", "Soldier (Blue) 1" },
                { "ヒゲオヤジ（紫）※宿屋", "Bearded Man (Purple) *Inn" },
                { "女の子（赤）※道具屋", "Girl (Red) *Item Shop" },
                { "男（青）※防具屋", "Man (Blue) *Armor Shop" },
                { "ジジイ（赤）※武器屋", "Old Man (Red) *Weapon Shop" },
                { "ジジイ（赤）④", "Old Man (Red) 4" },
                { "ババア（青）⑥", "Old Woman (Blue) 6" },
                { "女性（赤）⑨", "Woman (Red) 9" },
                { "男の子（青）⑤", "Boy (Blue) 5" },
                { "女の子（赤）⑧", "Girl (Red) 8" },
                { "男（青）⑩", "Man (Blue) 10" },
                { "女性（青）⑦", "Woman (Blue) 7" },
                { "ネミングウェイ（青）⑪", "Namingway (Blue) 11" },
                { "倒れている兵士（青）②", "Fallen Soldier (Blue) 2" },
                { "倒れている兵士（青）①", "Fallen Soldier (Blue) 1" },
                { "階段の一番上", "Top of Stairs" },
                { "エブラーナ王と王妃", "King and Queen of Eblan" },
                { "ギル", "Gil" },
                { "エブラーナ王", "King of Eblan" },
                { "エブラーナ王妃", "Queen of Eblan" },
                { "トリガーの位置を通ろうとする", "Try to Pass Trigger Position" },
                { "クリスタル（青）", "Crystal (Blue)" },
                { "クリスタル（黄）", "Crystal (Yellow)" },
                { "クリスタル（黄）右下用", "Crystal (Yellow) Bottom Right" },
                { "焚火2", "Bonfire 2" },
                { "焚火1", "Bonfire 1" },
                { "ククロ", "Kokkol" },
                { "シルフ②", "Sylph 2" },
                { "シルフ③", "Sylph 3" },
                { "シルフ④", "Sylph 4" },
                { "ヤン(イベント)", "Yang (Event)" },
                { "シルフ①", "Sylph 1" },
                { "ワールドマップへ", "To World Map" },
                { "幻獣イベント1", "Eidolon Event 1" },
                { "幻獣（紫）④", "Eidolon (Purple) 4" },
                { "幻獣（赤）③", "Eidolon (Red) 3" },
                { "幻獣（青）①", "Eidolon (Blue) 1" },
                { "幻獣（紫）②", "Eidolon (Purple) 2" },
                { "幻獣（赤）①", "Eidolon (Red) 1" },
                { "チョコボ（黄）②", "Chocobo (Yellow) 2" },
                { "幻獣（青）⑧", "Eidolon (Blue) 8" },
                { "チョコボ（黄）⑤", "Chocobo (Yellow) 5" },
                { "幻獣（赤）⑥", "Eidolon (Red) 6" },
                { "幻獣（青）⑦", "Eidolon (Blue) 7" },
                { "ボム（青）③", "Bomb (Blue) 3" },
                { "ボム（赤）④", "Bomb (Red) 4" },
                { "幻獣（ボム）※防具屋", "Eidolon (Bomb) *Armor Shop" },
                { "チョコボ（黄）※道具屋", "Chocobo (Yellow) *Item Shop" },
                { "幻獣（青）※宿屋", "Eidolon (Blue) *Inn" },
                { "幻獣（青）※武器屋", "Eidolon (Blue) *Weapon Shop" },
                { "幻獣王妃", "Eidolon Queen" },
                { "アイテム使用まで閉鎖", "Closed Until Item Used" },
                { "封印の洞窟の扉で『ルカの首飾り』を使用", "Use Luca's Necklace at Sealed Cavern Door" },
                { "アサルトドアー①", "Trap Door 1" },
                { "綱渡り①", "Rope Bridge 1" },
                { "綱渡り②", "Rope Bridge 2" },
                { "アサルトドアー②", "Trap Door 2" },
                { "綱渡り③", "Rope Bridge 3" },
                { "綱渡り④", "Rope Bridge 4" },
                { "アサルトドアー⑦", "Trap Door 7" },
                { "アサルトドアー④", "Trap Door 4" },
                { "アサルトドアー③", "Trap Door 3" },
                { "アサルトドアー⑤", "Trap Door 5" },
                { "アサルトドアー⑥", "Trap Door 6" },
                { "クリスタル", "Crystal" },
                { "回復ベッド", "Recovery Bed" },
                { "魔導船発進", "Lunar Whale Launch" },
                { "地上に出る", "Return to Surface" },
                { "でぶチョコボ", "Fat Chocobo" },
                { "でぶチョコボ会話用", "Fat Chocobo Conversation" },
                { "魔導船", "Lunar Whale" },
                { "魔導船出現デモ", "Lunar Whale Appearance Demo" },
                { "幻獣神の洞窟_1戦目", "Cave of Bahamut_Battle 1" },
                { "幻獣神", "Bahamut" },
                { "女の子（紫）", "Girl (Purple)" },
                { "男の子（紫）", "Boy (Purple)" },
                { "ネミングウェイ（青）⑨※会話にSEあり", "Namingway (Blue) 9 *With SE" },
                { "ネミングウェイ（青）⑧※会話にSEあり", "Namingway (Blue) 8 *With SE" },
                { "ネミングウェイ（青）⑩※会話にSEあり", "Namingway (Blue) 10 *With SE" },
                { "ネミングウェイ（青）①※道具屋", "Namingway (Blue) 1 *Item Shop" },
                { "ネミングウェイ（青）⑦※会話にSEあり", "Namingway (Blue) 7 *With SE" },
                { "ネミングウェイ（青）⑪※会話にSEあり", "Namingway (Blue) 11 *With SE" },
                { "ネミングウェイ（紫）③", "Namingway (Purple) 3" },
                { "ネミングウェイ（青）⑥※会話にSEあり", "Namingway (Blue) 6 *With SE" },
                { "ネミングウェイ（青）⑫※会話にSEあり", "Namingway (Blue) 12 *With SE" },
                { "ネミングウェイ（赤）④", "Namingway (Red) 4" },
                { "ネミングウェイ（赤）②", "Namingway (Red) 2" },
                { "HP回復", "HP Recovery" },
                { "MP回復", "MP Recovery" },
                { "フースーヤ", "Fusoya" },
                { "救済ショップ", "Emergency Shop" },
                { "四天王", "Four Fiends" },
                { "バルバリシア", "Barbariccia" },
                { "幻獣（スカルミリョーネ）", "Eidolon (Scarmiglione)" },
                { "制御装置", "Control System" },
                { "クリスタル（青）②", "Crystal (Blue) 2" },
                { "クリスタル（黄）①", "Crystal (Yellow) 1" },
                { "クリスタル（黄）③", "Crystal (Yellow) 3" },
                { "クリスタル（青）⑧", "Crystal (Blue) 8" },
                { "クリスタル（青）④", "Crystal (Blue) 4" },
                { "クリスタル（黄）⑦", "Crystal (Yellow) 7" },
                { "クリスタル（青）⑥", "Crystal (Blue) 6" },
                { "クリスタル（黄）⑤", "Crystal (Yellow) 5" },
                { "ヒゲオヤジ（赤）②", "Bearded Man (Red) 2" },
                { "女性（青）③", "Woman (Blue) 3" },
                { "男（赤）①", "Man (Red) 1" },
                { "女性（赤）③", "Woman (Red) 3" },
                { "ジジイ（青）②", "Old Man (Blue) 2" },
                { "武器屋用OpenTrigger", "Weapon Shop Open Trigger" },
                { "武器屋用GotoMap", "Weapon Shop Goto Map" },
                { "水路用GotoMap", "Waterway Goto Map" },
                { "水路用OpenTrigger", "Waterway Open Trigger" },
                { "男の子（赤）④", "Boy (Red) 4" },
                { "踊り子（服装　赤）", "Dancer (Clothed Red)" },
                { "水着（赤）", "Swimsuit (Red)" },
                { "女の子（赤）②", "Girl (Red) 2" },
                { "男（紫）④", "Man (Purple) 4" },
                { "門が開く", "Gate Opens" },
                { "ドアが開く（奥側）", "Door Opens (Back)" },

                // Consumable Items
                { "ポーション", "Potion" },
                { "ハイポーション", "Hi-Potion" },
                { "エーテル", "Ether" },
                { "エーテルドライ", "Dry Ether" },
                { "エリクサー", "Elixir" },
                { "テント", "Tent" },
                { "コテージ", "Cottage" },
                { "フェニックスのお", "Phoenix Down" },
                { "ばんのうやく", "Remedy" },
                { "めぐすり", "Eye Drops" },
                { "おとめのキッス", "Maiden's Kiss" },
                { "やまびこそう", "Echo Herbs" },
                { "きんのはり", "Gold Needle" },
                { "どうのすなどけい", "Hourglass" },
                { "ぎんのすなどけい", "Silver Hourglass" },
                { "めざましどけい", "Alarm Clock" },
                { "バッカスのさけ", "Bacchus's Wine" },
                { "バッカスの酒", "Bacchus's Wine" },
                { "ソーマのしずく", "Soma Drop" },
                { "アラーム", "Alarm" },
                { "こびとのパン", "Gnomish Bread" },
                { "スケープドール", "Decoy" },
                { "くものいと", "Spider's Silk" },

                // Gil Treasures
                { "960ギル", "960 Gil" },
                { "1000ギル", "1,000 Gil" },
                { "2000ギル", "2,000 Gil" },
                { "5000ギル", "5,000 Gil" },
                { "6000ギル", "6,000 Gil" },
                { "10000ギル", "10,000 Gil" },
                { "50000ギル", "50,000 Gil" },

                // Weapons
                { "アイスロッド", "Ice Rod" },
                { "アイスブランド", "Icebrand" },
                { "フレイムソード", "Flame Sword" },
                { "こだいのつるぎ", "Ancient Sword" },
                { "だいちのハンマー", "Gaia Hammer" },
                { "じごくのつめ", "Hell Claws" },
                { "ねこのつめ", "Cat Claws" },
                { "ようせいのつめ", "Fairy Claws" },
                { "しゅりけん", "Shuriken" },
                { "ふうましゅりけん", "Fuma Shuriken" },
                { "キラーボウ", "Killer Bow" },
                { "グレートボウ", "Great Bow" },
                { "エルフィンボウ", "Elfin Bow" },
                { "ポイズンアクス", "Poison Axe" },
                { "きくいちもんじ", "Kikuichimonji" },
                { "ディフェンダー", "Defender" },
                { "メイジマッシャー", "Mage Masher" },
                { "オーガキラー", "Ogrekiller" },
                { "ブラッドランス(バトルあり)", "Blood Lance (With Battle)" },
                { "ねむりのけん(バトルあり)", "Sleep Blade (With Battle)" },
                { "リリスのくちづけ", "Lilith's Kiss" },
                { "あしゅら", "Ashura" },
                { "へんげのロッド", "Polymorph Rod" },
                { "ほしくずのロッド", "Stardust Rod" },
                { "ファイアビュート", "Fire Lash" },
                { "けんじゃのつえ", "Sage's Staff" },
                { "ミスリルの杖", "Mythril Staff" },
                { "むらさめ", "Murasame" },
                { "まさむね", "Masamune" },
                { "らぐなろく", "Ragnarok" },
                { "ほーりーらんす", "Holy Lance" },
                { "アヴェンジャー", "Avenger" },
                { "あかいきば", "Red Fang" },
                { "しろいきば", "White Fang" },
                { "あおいきば", "Blue Fang" },
                { "えんげつりん", "Pinwheel" },
                { "メデューサのや", "Medusa Arrow" },
                { "くちふうじのや", "Mute Arrow" },

                // Arrows
                { "せいなるや", "Holy Arrow" },
                { "てんしのや", "Angel Arrow" },
                { "ほのおのや", "Fire Arrow" },
                { "いかづちのや", "Thunder Arrow" },
                { "こおりのや", "Ice Arrow" },
                { "よいちのや", "Yoichi Arrow" },
                { "よいちのゆみ", "Yoichi Bow" },
                { "アルテミスのや", "Artemis Arrow" },

                // Armor/Equipment
                { "エルメスのくつ", "Hermes Sandals" },
                { "グリーンベレー", "Green Beret" },
                { "しさいのローブ", "Sage's Robe" },
                { "くろおびどうぎ", "Black Belt Gi" },
                { "くろしょうぞく", "Black Garb" },
                { "ドラゴンシールド", "Dragon Shield" },
                { "ドラゴンヘルム", "Dragon Helm" },
                { "ドラゴンメイル", "Dragon Mail" },
                { "ドラゴンのこて", "Dragon Gloves" },
                { "クリスタルのたて", "Crystal Shield" },
                { "クリスタルメイル", "Crystal Mail" },
                { "クリスタルのこて", "Crystal Gloves" },
                { "クリスタルヘルム", "Crystal Helm" },
                { "まもりのゆびわ", "Protection Ring" },
                { "リボン", "Ribbon" },
                { "パワーリスト", "Power Armlet" },
                { "ドワーフのおの", "Dwarf's Axe" },
                { "げんじのこて", "Genji Gloves" },
                { "げんじのたて", "Genji Shield" },
                { "げんじのかぶと", "Genji Helm" },
                { "げんじのよろい", "Genji Armor" },
                { "ふく", "Clothing" },
                { "フレイムメイル", "Flame Mail" },
                { "フレイムシールド", "Flame Shield" },

                // Accessories/Special Items
                { "ボムの指輪", "Bomb Ring" },
                { "ボムのかけら", "Bomb Fragment" },
                { "ボムの魂", "Bomb Soul" },
                { "ボムのたましい", "Bomb Spirit" },
                { "ゼウスのいかり", "Zeus's Wrath" },
                { "ゼウスの怒り", "Zeus's Wrath" },
                { "なんきょくのかぜ", "Antarctic Wind" },
                { "ほっきょくのかぜ", "Arctic Wind" },
                { "ユニコーンのつの", "Unicorn Horn" },
                { "クアールのひげ", "Coeurl Whisker" },
                { "バンパイアのきば", "Vampire Fang" },
                { "ネズミのしっぽ", "Rat Tail" },
                { "ぎんのリンゴ", "Silver Apple" },
                { "ぎんのリンゴ(バトルあり)", "Silver Apple (With Battle)" },
                { "金のリンゴ", "Golden Apple" },
                { "きんのリンゴ", "Golden Apple" },
                { "きんのかみかざり", "Gold Hairpin" },
                { "ルビーのゆびわ", "Ruby Ring" },
                { "星の砂", "Stardust" },
                { "月のカーテン", "Lunar Curtain" },
                { "かいじゅうずかん", "Bestiary" },
                { "怪獣図鑑", "Bestiary" },

                // NPCs - Base Forms
                { "神官（紫）", "Priest (Purple)" },
                { "モンク僧（青）", "Monk (Blue)" },
                { "ドワーフ兵（赤）", "Dwarf Soldier (Red)" },
                { "ドワーフ王", "Dwarf King" },
                { "兵士（青）", "Soldier (Blue)" },
                { "倒れている兵士（青）", "Fallen Soldier (Blue)" },
                { "女の子（赤）", "Girl (Red)" },
                { "女の子（青）", "Girl (Blue)" },
                { "男の子（赤）", "Boy (Red)" },
                { "男（紫）", "Man (Purple)" },
                { "近衛兵士（青）", "Royal Guard (Blue)" },
                { "ボム（赤）", "Bomb (Red)" },
                { "アサルトドアー", "Trap Door" },
                { "黒チョコボ", "Black Chocobo" },

                // NPCs - Troia Castle Positional Variants
                { "神官（紫）右下", "Priest (Purple) Bottom Right" },
                { "神官（紫）左下", "Priest (Purple) Bottom Left" },
                { "神官（紫）右中", "Priest (Purple) Middle Right" },
                { "神官（紫）左中", "Priest (Purple) Middle Left" },
                { "神官（紫）上左", "Priest (Purple) Upper Left" },
                { "神官（紫）上右", "Priest (Purple) Upper Right" },
                { "神官（紫）左上", "Priest (Purple) Top Left" },
                { "神官（紫）右上", "Priest (Purple) Top Right" },

                // Named Characters/Bosses
                { "バロン王", "King Baron" },
                { "エッジ", "Edge" },
                { "ゼムス", "Zemus" },
                { "じい", "Old Man" },

                // Events/Story Triggers
                { "ミストドラゴン戦闘", "Mist Dragon Battle" },
                { "プロローグ１（ローザイベント）", "Prologue 1 (Rosa Event)" },
                { "プロローグ２（シドイベント）", "Prologue 2 (Cid Event)" },
                { "ファーブル防衛戦後1", "After Fabul Defense 1" },
                { "ローザ救出", "Rosa Rescue" },
                { "バルバリシア戦闘", "Barbariccia Battle" },
                { "土のクリスタルを届ける", "Deliver Earth Crystal" },
                { "ルゲイエのカギ使用イベント", "Dr. Lugae's Key Use Event" },
                { "大砲室から追い出される", "Expelled from Cannon Room" },
                { "0059：ファルコン号発進", "0059: Falcon Launch" },
                { "ファルコン号発進", "Falcon Launch" },
                { "シド再会", "Cid Reunion" },
                { "ドリル取り付け", "Drill Installation" },
                { "カインの裏切り", "Kain's Betrayal" },
                { "エッジとルビカンテ", "Edge and Rubicante" },
                { "ゾットの塔へ", "To Tower of Zot" },
                { "エンタープライズ号", "Enterprise" },
                { "地下への道開通", "Underground Path Opens" },
                { "ドリルで地上への穴をあける", "Drill to Surface" },
                { "最後の戦い", "The Final Battle" },
                { "幻獣神の洞窟_2戦目", "Cave of Bahamut Battle 2" },
                { "デモンズウォール", "Demon Wall" },
                { "幻獣1", "Eidolon 1" },
                { "幻獣2", "Eidolon 2" },
                { "眠りマーク", "Sleep Mark" },
                { "OP用：近衛兵士（赤）1", "OP: Royal Guard (Red) 1" },

                // Vehicles/Transportation
                { "ファルコン号", "Falcon" },
                { "塔（地底）", "Tower (Underground)" },
                { "月の魔導船から降りる", "Disembark Lunar Whale" },
                { "飛行モードにするレバー", "Flight Mode Lever" },
                { "クリスタル（赤）デフォルト", "Crystal (Red) Default" },
                { "月の地下渓谷入口", "Lunar Subterrane Entrance" }
            };

            isInitialized = true;
        }

        /// <summary>
        /// Translates a Japanese entity name to English.
        /// Returns original name if no translation found.
        /// </summary>
        public static string Translate(string japaneseName)
        {
            if (string.IsNullOrEmpty(japaneseName))
                return japaneseName;

            // Ensure initialized
            if (!isInitialized)
                Initialize();

            // 1. Exact match first (preserves existing behavior)
            if (translations.TryGetValue(japaneseName, out string englishName))
                return englishName;

            // 2. Strip numeric/SC prefix and try base name lookup
            StripPrefix(japaneseName, out string prefix, out string baseName);
            if (prefix != null && translations.TryGetValue(baseName, out string baseTranslation))
                return prefix + " " + baseTranslation;

            // 3. Strip circled number SUFFIX and try base name lookup
            StripSuffix(japaneseName, out string suffix, out string baseNameNoSuffix);
            if (suffix != null && translations.TryGetValue(baseNameNoSuffix, out string baseSuffixTranslation))
                return baseSuffixTranslation + " " + ConvertCircledNumber(suffix);

            // 4. Handle both prefix AND suffix (e.g., "SC01:白魔道士（赤）④")
            if (prefix != null)
            {
                StripSuffix(baseName, out string innerSuffix, out string innerBase);
                if (innerSuffix != null && translations.TryGetValue(innerBase, out string innerTranslation))
                    return prefix + " " + innerTranslation + " " + ConvertCircledNumber(innerSuffix);
            }

            // Return original if no translation
            return japaneseName;
        }

        /// <summary>
        /// Strips a numeric or SC prefix from an entity name.
        /// Returns the prefix (e.g., "6:" or "SC01:") and the base name.
        /// If no prefix is found, prefix will be null and baseName will equal the input.
        /// </summary>
        private static void StripPrefix(string name, out string prefix, out string baseName)
        {
            Match match = EntityPrefixRegex.Match(name);
            if (match.Success)
            {
                prefix = match.Groups[1].Value;
                baseName = name.Substring(prefix.Length);
            }
            else
            {
                prefix = null;
                baseName = name;
            }
        }

        /// <summary>
        /// Strips a circled number suffix from an entity name.
        /// Returns the suffix (e.g., "④") and the base name.
        /// If no suffix is found, suffix will be null and baseName will equal the input.
        /// </summary>
        private static void StripSuffix(string name, out string suffix, out string baseName)
        {
            Match match = EntitySuffixRegex.Match(name);
            if (match.Success)
            {
                suffix = match.Groups[1].Value;
                baseName = name.Substring(0, name.Length - suffix.Length);
            }
            else
            {
                suffix = null;
                baseName = name;
            }
        }

        private static readonly Dictionary<char, string> CircledNumberMap = new Dictionary<char, string>
        {
            {'①', "1"}, {'②', "2"}, {'③', "3"}, {'④', "4"}, {'⑤', "5"},
            {'⑥', "6"}, {'⑦', "7"}, {'⑧', "8"}, {'⑨', "9"}, {'⑩', "10"},
            {'⑪', "11"}, {'⑫', "12"}, {'⑬', "13"}, {'⑭', "14"}, {'⑮', "15"},
            {'⑯', "16"}, {'⑰', "17"}, {'⑱', "18"}, {'⑲', "19"}, {'⑳', "20"}
        };

        /// <summary>
        /// Converts circled number (①②③...) to regular digit string.
        /// </summary>
        private static string ConvertCircledNumber(string circled)
        {
            if (circled.Length == 1 && CircledNumberMap.TryGetValue(circled[0], out string num))
                return num;
            return circled;
        }

        /// <summary>
        /// Gets the count of loaded translations.
        /// </summary>
        public static int TranslationCount => translations?.Count ?? 0;
    }
}
