using Newtonsoft.Json;
using Oxide.Core;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{

    [Info("GhostBan", "Bobakanoosh", "1.0.1")]
    [Description("Ghost ban rule breakers causing them to not do damage to other players")]
    class GhostBan : RustPlugin
    {
        private const string DATA_FILE = "GhostBanData";
        private const string PERMISSION_BAN = "ghostban.ban";
        private const string PERMISSION_UNBAN = "ghostban.unban";
        private const string PERMISSION_CHECK = "ghostban.check";

        private PluginConfig config;
        private GhostBanStoredData ghostBanStoredData;
        private Dictionary<BasePlayer, Ban> playerToBan = new Dictionary<BasePlayer, Ban>();

        #region Init

        private void Init()
        {
            LoadConfig();
            LoadData();

            permission.RegisterPermission(PERMISSION_BAN, this);
            permission.RegisterPermission(PERMISSION_UNBAN, this);
            permission.RegisterPermission(PERMISSION_CHECK, this);
        }

        private void OnServerInitialized()
        {
            foreach(BasePlayer player in BasePlayer.activePlayerList)
            {
                OnPlayerConnected(player);
            }

            if(config.enableRandomDrop)
            {
                timer.Every(config.dropInterval, () => TryDrop());
            }

            CheckUnsubscribe();
        }

        #endregion

        #region Hooks
        private void OnPlayerConnected(BasePlayer player)
        {
            Ban ban = ghostBanStoredData.bans.FirstOrDefault(b => b.playerId == player.userID);
            if(ban != null)
            {
                playerToBan[player] = ban;
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            playerToBan.Remove(player);
        }

        object OnEntityTakeDamage(BasePlayer victim, HitInfo info)
        {
            BasePlayer attacker = info.InitiatorPlayer;
            if(attacker.IsValid() && playerToBan.ContainsKey(attacker) && victim.IsValid() && !victim.IsNpc)
            {
                if(config.enableTeamDamage && victim.currentTeam == attacker.currentTeam)
                {
                    return null;
                }

                return false;
            }

            return null;
        }

        #endregion

        #region Commands

        [ChatCommand("ghost.ban")]
        private void GhostBanCommand(BasePlayer player, string command, string[] args)
        {
            string message;
            BasePlayer target = CommandPreCheck(player, command, args, PERMISSION_BAN, out message);
            if (target == null)
            {
                Message(player, message);
                return;
            }

            if (config.blockIfTargetPerm && permission.UserHasPermission(target.UserIDString, PERMISSION_BAN))
            {
                Message(player, "UnableToTarget", target.displayName);
                return;
            }

            if(config.blockSameAuthLevel && player.Connection.authLevel <= target.Connection.authLevel)
            {
                Message(player, "UnableToTarget", target.displayName);
                return; 
            }

            if(playerToBan.ContainsKey(target))
            {
                Message(player, "AlreadyGhostBanned", target.displayName);
                return;
            }

            AddGhostBan(player, target);
            Message(player, "SuccessGhostBan", target.displayName);

        }

        [ChatCommand("ghost.unban")]
        private void GhostUnbanCommand(BasePlayer player, string command, string[] args)
        {
            string message;
            BasePlayer target = CommandPreCheck(player, command, args, PERMISSION_UNBAN, out message);
            if (target == null)
            {
                Message(player, message);
                return;
            }

            if (!RemoveGhostBan(target))
            {
                Message(player, "NotGhostBanned", target.displayName);
                return;
            }

            Message(player, "RemovedGhostBan", target.displayName);
        }

        [ChatCommand("ghost.check")]
        private void GhostCheckCommand(BasePlayer player, string command, string[] args)
        {
            string message;
            BasePlayer target = CommandPreCheck(player, command, args, PERMISSION_CHECK, out message);
            if(target == null)
            {
                Message(player, message);
                return;
            }

            Ban ban;
            if(!playerToBan.TryGetValue(target, out ban))
            {
                Message(player, "NotGhostBanned", target.displayName);
                return;
            }

            BasePlayer banner = BasePlayer.FindAwakeOrSleeping(ban.bannerId.ToString());
            Message(player, "GhostBannedBy", target.displayName, banner?.displayName ?? "null");

        }

        #endregion

        #region Helpers
        private void TryDrop()
        {
            foreach(KeyValuePair<BasePlayer, Ban> pair in playerToBan)
            {
                BasePlayer player = pair.Key;

                if (player.IsValid())
                {
                    int rand = UnityEngine.Random.Range(0, 1000);
                    if(rand < (int)(config.percentDropChance * 1000))
                    {
                        Item[] items = player.inventory.AllItems();
                        int itemIndex = UnityEngine.Random.Range(0, items.Length);

                        items[itemIndex].Drop(player.transform.position, player.estimatedVelocity);

                    }
                }
            }
        }

        private void AddGhostBan(BasePlayer banner, BasePlayer target)
        {
            Ban ban = new Ban { playerId = target.userID, bannerId = banner.userID };

            playerToBan[target] = ban;
            ghostBanStoredData.bans.Add(ban);

            Subscribe(nameof(OnEntityTakeDamage));

            SaveData();
        }

        private bool RemoveGhostBan(BasePlayer target)
        {
            Ban ban;
            if(playerToBan.TryGetValue(target, out ban))
            {
                ghostBanStoredData.bans.Remove(ban);
                playerToBan.Remove(target);

                CheckUnsubscribe();

                SaveData();
                return true;
            }

            return false;
        }

        private BasePlayer CommandPreCheck(BasePlayer player, string command, string[] args, string perm, out string message)
        {
            if (permission.UserHasPermission(player.UserIDString, perm))
            {
                message = "You don't have permission to use that command";
                return null;
            }

            if (args.Length < 1)
            {
                message = $"Argument mismatch. Use /{command} <target | userid>";
                return null;
            }

            BasePlayer target = BasePlayer.Find(args[0]);
            if (target == null)
            {
                message = $"Invalid target: {args[0]}";
                return null;
            }

            message = "";
            return target;
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(DATA_FILE, ghostBanStoredData);
        private void LoadData() => ghostBanStoredData = Interface.Oxide.DataFileSystem.ReadObject<GhostBanStoredData>(DATA_FILE);

        private void CheckUnsubscribe()
        {
            if (playerToBan.Count == 0)
            {
                Unsubscribe(nameof(OnEntityTakeDamage));
            }
        }

        public void Message(BasePlayer player, string message, params object[] args)
        {
            if (player.IsValid())
            {
                message = lang.GetMessage(message, this);

                if (args.Length > 0)
                    message = string.Format(message, args);
                SendReply(player, $"{config.prefix}{message}");
            }
        }

        #endregion

        #region Classes

        private class GhostBanStoredData
        {
            public HashSet<Ban> bans = new HashSet<Ban>();
        }

        private class Ban
        {
            public ulong playerId;
            public ulong bannerId;

            public Ban() { }
        }

        #endregion

        #region Config

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<PluginConfig>();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        protected override void LoadDefaultConfig()
        {
            config = new PluginConfig
            {
                prefix = "[ <color=#ff5500>GhostBan</color> ] ",    
                blockIfTargetPerm = true,
                blockSameAuthLevel = true,
                enableTeamDamage = true,
                enableRandomDrop = false,
                percentDropChance = 0.50f,
                dropInterval = 60f
            };

            SaveConfig();
        }

        private class PluginConfig
        {
            [JsonProperty("Plugin message prefix")]
            public string prefix;

            [JsonProperty("Block ghost banning a user who has the ghostban.use permission")]
            public bool blockIfTargetPerm;

            [JsonProperty("Block ghost banning a user who has the same or higher auth level as themself")]
            public bool blockSameAuthLevel;

            [JsonProperty("When enabled, if a ghost banned player attacks someone on their team, it does damage")]
            public bool enableTeamDamage;

            [JsonProperty("When enabled, ghost banned players will randomly have items from their inventory drop")]
            public bool enableRandomDrop;

            [JsonProperty("% chance to drop a players item [0.00 - 1.00]")]
            public float percentDropChance;

            [JsonProperty("Interval in seconds to try and drop the target players items")]
            public float dropInterval;

        }

        #endregion

        #region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["UnableToTarget"] = "Unable to target {0}",
                ["AlreadyGhostBanned"] = "{0} is already ghost banned",
                ["SuccessGhostBan"] = "Ghost banned {0}",
                ["NotGhostBanned"] = "{0} is not ghost banned",
                ["RemovedGhostBan"] = "Successfully removed {0}'s ghost ban",
                ["GhostBannedBy"] = "{0} was ghost banned by {1}"
            }, this);
        }

        #endregion

    }
}
