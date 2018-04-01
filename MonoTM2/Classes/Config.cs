using System;
using System.Collections.Generic;
using System.IO;
using MonoTM2.Classes.Hosts;
using MonoTM2.InputOutput;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MonoTM2.Classes
{
    [JsonObject(Title = "RootObject")]
    public class Config
    {
        [JsonIgnore]
        private static Config m_config;
        [JsonIgnore]
        private static object syncRoot = new object();

        public SteamSettings SteamSettings = new SteamSettings();

        //[JsonConverter(typeof(DictionarySettingConverter))]
        public Dictionary<string, Market> MarketsSettings = new Dictionary<string, Market>();

        /// <summary>
		/// Апи ключ маркета
		/// </summary>
        public string key { get; set; } = "";

        /// <summary>
        /// таймер между вещами(мс)
        /// </summary>
        public int Itimer { get; set; } = 300;



        /// <summary>
		/// Время между отсылкой уведомлений сайту(мин.)
		/// </summary>
        public double PingPongTimerTime { get; set; } = 0.5;

        public Dictionary<MessageType, bool> Messages = new Dictionary<MessageType, bool>();

        public static void Reload()
        {
            if (File.Exists("config.json"))
                m_config = JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));
            else
                m_config = new Config();

            CheckMessage();
            MigrateSteamSetting();
            CreateMarketPlaces();
        }

        public static void Save()
        {
            File.WriteAllText("config.json", JsonConvert.SerializeObject(m_config,Formatting.Indented));
        }

        public static Config GetConfig()
        {
            if (m_config == null)
            {
                lock (syncRoot)
                {
                    if (m_config == null)
                        Reload();
                }
            }

            return m_config;
        }

        private Config() { }

        public bool IsEnabledMessage(MessageType msgType)
        {
            return Messages[msgType];
        }

        private static void CheckMessage()
        {
            if (m_config.Messages.Count == 0 || m_config.Messages.Count != Enum.GetNames(typeof(MessageType)).Length)
            {
                var mm = Enum.GetValues(typeof(MessageType));
                foreach (var it in mm)
                {
                    try
                    {
                        m_config.Messages.Add((MessageType)it, true);
                    }
                    catch { }
                }

                Save();
            }
        }

        private static void MigrateSteamSetting()
        {
            JObject results = JObject.Parse(File.ReadAllText("config.json"));
            //TODO условие, когда не хочешь заполнять стим
            if (string.IsNullOrEmpty(m_config.SteamSettings.Login) && results["SteamLogin"] != null)
            {
                m_config.SteamSettings.Login = results["SteamLogin"].ToString();
                m_config.SteamSettings.Password = results["SteamPassword"].ToString();
                m_config.SteamSettings.MachineAuth = results["SteamMachineAuth"].ToString();
                m_config.SteamSettings.ApiKey = results["SteamApiKey"].ToString();
                m_config.SteamSettings.TimeoutRelogin = results["SteamTimeOutRelogin"].ToObject<int>();

                Save();
            }
        }

        private static void CreateMarketPlaces()
        {
            if (m_config.MarketsSettings.Count == 0)
            {
                m_config.MarketsSettings.Add(Const.Host.CSGO,  new Market());
                m_config.MarketsSettings.Add(Const.Host.DOTA2, new Market());
                m_config.MarketsSettings.Add(Const.Host.PUBG,  new Market());

                Save();
            }
            else
            {
                if (m_config.MarketsSettings.Count == 3) return;

                if(!m_config.MarketsSettings.ContainsKey(Const.Host.CSGO))
                    m_config.MarketsSettings.Add(Const.Host.CSGO, new Market());

                if (!m_config.MarketsSettings.ContainsKey(Const.Host.DOTA2))
                    m_config.MarketsSettings.Add(Const.Host.DOTA2, new Market());

                if (!m_config.MarketsSettings.ContainsKey(Const.Host.PUBG))
                    m_config.MarketsSettings.Add(Const.Host.PUBG, new Market());

                Save();
            }
        }
    }
}
