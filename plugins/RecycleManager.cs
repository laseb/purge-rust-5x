using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oxide.Core.Configuration;
using UnityEngine;
using Oxide.Core;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("RecycleManager", "Pho3niX90", "1.1.2")]
    [Description("Easily change features about the recycler")]

    class RecycleManager : RustPlugin
    {
        private bool changed;

        #region Data

        private DynamicConfigFile OutputData;
        private StoredData storedData;

        private void SaveData() {
            storedData.table = ingredientList;
            OutputData.WriteObject(storedData);
        }

        private void LoadData() {
            try {
                storedData = OutputData.ReadObject<StoredData>();
                ingredientList = storedData.table;
            } catch {
                Puts("Old data file found, will attempt converting it.");
                try {
                    storedData = new StoredData();
                    var oldData = OutputData.ReadObject<StoredData_PreVersion_1_1_0>();

                    foreach (var item in oldData.table) {
                        var rustItemInfo = ItemManager.itemList.FirstOrDefault(x => x.shortname.Equals(item.Key));

                        ItemInfo newItemInfo = rustItemInfo != null
                            ? new ItemInfo {
                                itemName = item.Key,
                                useGlobalMaxItemsPerRecycle = true,
                                maxItemsPerRecycle = Mathf.CeilToInt(Mathf.Max(1f, (float)rustItemInfo.stackable * 0.1f)),
                                ingredients = new List<ItemIngredient>()
                            }
                        : new ItemInfo {
                            itemName = item.Key,
                            ingredients = new List<ItemIngredient>()
                        };

                        foreach (var ing in item.Value) {
                            newItemInfo.ingredients.Add(ing);
                        }

                        storedData.table.Add(newItemInfo);
                    }
                    ingredientList = storedData.table;
                    Puts("Convertion done.");
                } catch {
                    Puts("Failed to load data, creating new file");
                    storedData = new StoredData();
                }
            }
        }

        private class StoredData
        {
            public List<ItemInfo> table = new List<ItemInfo>();
        }
        private class StoredData_PreVersion_1_1_0
        {
            public Dictionary<string, List<ItemIngredient>> table = new Dictionary<string, List<ItemIngredient>>();
        }

        #endregion

        private class ItemInfo
        {
            public string itemName;
            public int maxItemsPerRecycle;
            public bool useGlobalMaxItemsPerRecycle;
            public List<ItemIngredient> ingredients;
        }

        private class ItemIngredient
        {
            public string itemName;
            public int itemAmount;
        }

        public float recycleTime = 5.0f;
        private const string permissionNameADMIN = "recyclemanager.admin";
        private const string permissionNameCREATE = "recyclemanager.create";
        private int maxItemsPerRecycle = 100;

        private static Dictionary<string, object> Multipliers() {
            var at = new Dictionary<string, object> { { "*", 1 }, { "metal.refined", 1 } };
            return at;
        }

        private static List<object> Blacklist() {
            var at = new List<object> { "hemp.seed" };
            return at;
        }

        private static List<object> OutputBlacklist() {
            var at = new List<object> { "hemp.seed" };
            return at;
        }

        private List<object> blacklistedItems;
        private List<object> outputBlacklistedItems;
        private Dictionary<string, object> multiplyList;
        private List<ItemInfo> ingredientList = new List<ItemInfo>();

        private void LoadVariables() {
            blacklistedItems = (List<object>)GetConfig("Lists", "Input Blacklist", Blacklist());
            recycleTime = Convert.ToSingle(GetConfig("Settings", "Recycle Time", 5.0f));
            multiplyList = (Dictionary<string, object>)GetConfig("Lists", "Recycle Output Multipliers", Multipliers());
            maxItemsPerRecycle = Convert.ToInt32(GetConfig("Settings", "Max Items Per Recycle", 100));
            outputBlacklistedItems = (List<object>)GetConfig("Lists", "Output Blacklist", OutputBlacklist());

            if (!changed) return;
            SaveConfig();
            changed = false;
        }

        protected override void LoadDefaultConfig() {
            Config.Clear();
            LoadVariables();
        }

        private void Init() {
            LoadVariables();
            permission.RegisterPermission(permissionNameADMIN, this);
            permission.RegisterPermission(permissionNameCREATE, this);
            OutputData = Interface.Oxide.DataFileSystem.GetFile("RecycleManager");
            LoadData();

            lang.RegisterMessages(new Dictionary<string, string> {
                //chat
                ["No Permissions"] = "You cannot use this command!",
                ["addrecycler CONSOLE invalid syntax"] = "Invalid syntax! addrecycler <playername/id>",
                ["No Player Found"] = "No player was found or they are offline",
                ["AddRecycler CONSOLE success"] = "A recycler was successfully placed at the players location!",
                ["AddRecycler CannotPlace"] = "You cannot place a recycler here",
                ["RemoveRecycler CHAT NoEntityFound"] = "There were no valid entities found",
                ["RemoveRecycler CHAT EntityWasRemoved"] = "The targeted entity was removed",

            }, this);
        }

        private void OnServerInitialized() {
            if (ingredientList.Count == 0) {
                RefreshIngredientList();
            } else {
                UpdateIngredientList();
            }
        }

        [ChatCommand("addrecycler")]
        private void AddRecyclerCMD(BasePlayer player, string command, String[] args) {
            if (!permission.UserHasPermission(player.UserIDString, permissionNameCREATE) && !permission.UserHasPermission(player.UserIDString, permissionNameADMIN)) {
                player.ChatMessage(msg("No Permissions", player.UserIDString));
                return;
            }

            if (!player.IsBuildingAuthed()) {
                if (!permission.UserHasPermission(player.UserIDString, permissionNameADMIN)) {
                    player.ChatMessage(msg("AddRecycler CannotPlace", player.UserIDString));
                    return;
                }
            }

            BaseEntity ent = GameManager.server.CreateEntity("assets/bundled/prefabs/static/recycler_static.prefab", player.transform.position, player.GetNetworkRotation(), true);
            ent.Spawn();
            return;
        }

        [ConsoleCommand("recyclemanager.addrecycler")]
        private void AddRecyclerCMDConsole(ConsoleSystem.Arg arg) {
            if (arg?.Args == null) {
                arg.ReplyWith(msg("addrecycler CONSOLE invalid syntax"));
                return;
            }
            if (arg.Connection != null) return;
            if (arg.Args.Length != 1) {
                arg.ReplyWith(msg("addrecycler CONSOLE invalid syntax"));
                return;
            }
            BasePlayer target = FindPlayer(arg.Args[0]);
            if (target == null || !target.IsValid()) {
                arg.ReplyWith(msg("No Player Found"));
                return;
            }
            BaseEntity ent = GameManager.server.CreateEntity("assets/bundled/prefabs/static/recycler_static.prefab", target.transform.position, target.GetNetworkRotation(), true);
            ent.Spawn();
            arg.ReplyWith(msg("AddRecycler CONSOLE success"));
        }

        [ChatCommand("removerecycler")]
        private void RemoveRecyclerCMD(BasePlayer player, string command, string[] args) {
            if (!permission.UserHasPermission(player.UserIDString, permissionNameADMIN)) {
                player.ChatMessage(msg("No Permissions", player.UserIDString));
                return;
            }
            RaycastHit hit;
            Physics.Raycast(player.eyes.HeadRay(), out hit);
            if (hit.GetEntity() == null) {
                player.ChatMessage(msg("RemoveRecycler CHAT NoEntityFound", player.UserIDString));
                return;
            }
            BaseEntity ent = hit.GetEntity();
            if (!ent.name.Contains("recycler")) {
                player.ChatMessage(msg("RemoveRecycler CHAT NoEntityFound", player.UserIDString));
                return;
            }
            ent.Kill();
            player.ChatMessage(msg("RemoveRecycler CHAT EntityWasRemoved", player.UserIDString));
        }

        [ConsoleCommand("recyclemanager.reloadingredientlist")]
        private void reloadDataCONSOLECMD(ConsoleSystem.Arg args) {
            if (!args.IsServerside || !args.IsAdmin) return;
            OutputData = Interface.Oxide.DataFileSystem.GetFile("RecycleManager");
            LoadData();
            Puts("Recycler output list has successfully been updated!");
        }

        [ConsoleCommand("recyclemanager.updateingredientlist")]
        private void UpdateIngredientListCMD(ConsoleSystem.Arg arg) {
            if (!arg.IsServerside || !arg.IsAdmin) return;
            UpdateIngredientList();
            Puts("Recycler ingredients list has been updated!");
        }

        [ConsoleCommand("recyclemanager.refreshingredientlist")]
        private void RefreshIngredientListCMD(ConsoleSystem.Arg arg) {
            if (!arg.IsServerside || !arg.IsAdmin) return;
            RefreshIngredientList();
            Puts("Recycler ingredients list has been cleared and refreshed!");
        }

        private void OnRecyclerToggle(Recycler recycler, BasePlayer player) {
            if (recycler.IsOn()) {
                recycler.CancelInvoke("RecycleThink");
                return;
            }
            recycler.CancelInvoke("RecycleThink");
            timer.Once(0.1f, () => { recycler.Invoke("RecycleThink", recycleTime); });
        }

        private object CanRecycle(Recycler recycler, Item item) {
            bool stopRecycle = true;
            for (int i = 0; i < 6; i++) {
                Item slot = recycler.inventory.GetSlot(i);
                if (slot == null)
                    continue;

                if (!blacklistedItems.Contains(slot.info.shortname) && ItemInfoExists(slot.info.shortname)) {
                    stopRecycle = false;
                    break;
                }
            }
            return !stopRecycle;
        }

        private object OnRecycleItem(Recycler recycler, Item item) {
            var itemInfo = GetItemInfo(item.info.shortname);
            if (itemInfo == null || blacklistedItems.Contains(item.info.shortname)) {
                item.Drop(recycler.transform.TransformPoint(new Vector3(-0.3f, 1.7f, 1f)), Vector3.up, new Quaternion());
                return false;
            }

            bool flag = false;

            int usedItems = item.amount;

            if (!itemInfo.useGlobalMaxItemsPerRecycle) {
                if (usedItems > itemInfo.maxItemsPerRecycle)
                    usedItems = itemInfo.maxItemsPerRecycle;
            } else {
                if (usedItems > maxItemsPerRecycle)
                    usedItems = maxItemsPerRecycle;
            }

            item.UseItem(usedItems);
            foreach (var ingredient in itemInfo.ingredients) {
                double multi = 1;
                if (multiplyList.ContainsKey("*"))
                    multi = Convert.ToDouble(multiplyList["*"]);
                if (multiplyList.ContainsKey(ingredient.itemName))
                    multi = Convert.ToDouble(multiplyList[ingredient.itemName]);

                int outputamount = Convert.ToInt32(usedItems * Convert.ToDouble(ingredient.itemAmount) * multi);
                var output = Interface.CallHook("OnRecycleItemOutput", ingredient.itemName, outputamount);
                if (output != null) {
                    outputamount = (int)output;
                }
                if (outputamount < 1)
                    continue;
                if (!recycler.MoveItemToOutput(ItemManager.CreateByName(ingredient.itemName, outputamount)))
                    flag = true;
            }
            if (flag || !recycler.HasRecyclable()) {
                recycler.StopRecycling();
                for (int i = 5; i <= 11; i++) {
                    Item _item = recycler.inventory.GetSlot(i);
                    if (_item == null) continue;
                    if (_item.IsValid())
                        if (outputBlacklistedItems.Contains(_item.info.shortname)) {
                            _item.Remove();
                            _item.RemoveFromContainer();
                        }
                }
            }
            return true;
        }
        List<ItemIngredient> GetIngredients(ItemDefinition itemInfo) {
            if (itemInfo.Blueprint == null)
                return null;
            List<ItemIngredient> ing = new List<ItemIngredient>();

            itemInfo.Blueprint.ingredients.ForEach(slot => {
                int num = 1;
                if (slot.amount > 1) {
                    num = Mathf.CeilToInt(Mathf.Min((float)slot.amount, (float)itemInfo.stackable * 0.1f));
                }
                if (slot.itemDef.shortname != "scrap") {
                    ing.Add(new ItemIngredient {
                        itemAmount = Mathf.CeilToInt((slot.amount / 2) / itemInfo.Blueprint.amountToCreate),
                        itemName = slot.itemDef.shortname
                    });
                } else {
                    // Mathf.CeilToInt(Mathf.Min((float)slot.amount, (float)slot.info.stackable * 0.1f)
                    ing.Add(new ItemIngredient {
                        itemAmount = itemInfo.Blueprint.scrapFromRecycle,
                        itemName = slot.itemDef.shortname
                    });
                }
                // 240
            });
            return ing;
        }

        ItemInfo GetItemInfo(string shortName) => ingredientList.FirstOrDefault(x => x.itemName == shortName);
        bool ItemInfoExists(string shortName) => ingredientList.Count(x => x.itemName == shortName) > 0;

        private void RefreshIngredientList() {
            ingredientList.Clear();
            foreach (ItemDefinition itemInfo in ItemManager.itemList) {
                List<ItemIngredient> x = GetIngredients(itemInfo);
                if (x == null || x?.Count == 0) continue;
                ingredientList.Add(new ItemInfo {
                    itemName = itemInfo.shortname,
                    maxItemsPerRecycle = Mathf.CeilToInt(Mathf.Min(1f, (float)itemInfo.stackable * 0.1f)),
                    useGlobalMaxItemsPerRecycle = true,
                    ingredients = x
                });
            }
            SaveData();
        }

        private void UpdateIngredientList() {
            foreach (ItemDefinition itemInfo in ItemManager.itemList) {
                if (ItemInfoExists(itemInfo.shortname)) continue;
                List<ItemIngredient> x = GetIngredients(itemInfo);
                if (x == null || x?.Count == 0) continue;
                ingredientList.Add(new ItemInfo {
                    itemName = itemInfo.shortname,
                    maxItemsPerRecycle = Mathf.CeilToInt(Mathf.Min(1f, (float)itemInfo.stackable * 0.1f)),
                    useGlobalMaxItemsPerRecycle = true,
                    ingredients = x
                });
            }
            SaveData();
            LoadData();
        }

        private object GetConfig(string menu, string datavalue, object defaultValue) {
            var data = Config[menu] as Dictionary<string, object>;
            if (data == null) {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                changed = true;
            }
            object value;
            if (data.TryGetValue(datavalue, out value)) return value;
            value = defaultValue;
            data[datavalue] = value;
            changed = true;
            return value;
        }

        private static BasePlayer FindPlayer(string nameOrId) {
            foreach (var activePlayer in BasePlayer.activePlayerList) {
                if (activePlayer.UserIDString == nameOrId)
                    return activePlayer;
                if (activePlayer.displayName.Contains(nameOrId, CompareOptions.OrdinalIgnoreCase))
                    return activePlayer;
                if (activePlayer.net?.connection != null && activePlayer.net.connection.ipaddress == nameOrId)
                    return activePlayer;
            }
            return null;
        }

        private string msg(string key, string id = null) => lang.GetMessage(key, this, id);
    }
}