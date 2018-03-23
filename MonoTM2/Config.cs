using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using MonoTM2.InputOutput;

namespace MonoTM2
{
    [JsonObject(Title = "RootObject")]
    public class Config
    {
        [JsonIgnore]
        private static Config m_config;
        [JsonIgnore]
        private static object syncRoot = new Object();

        public string SteamLogin { get; set; } = "";
        public string SteamPassword { get; set; } = "";
        public string SteamMachineAuth { get; set; } = "";
        public string SteamApiKey { get; set; } = "";
        /// <summary>
        /// Таймаут переавторизации в стиме
        /// Чтобы не словить бан за частые перезаходы
        /// </summary>
        public int SteamTimeOutRelogin { get; set; } = 180;

        /// <summary>
		/// Апи ключ маркета
		/// </summary>
        public string key { get; set; } = "";

        /// <summary>
        /// Прибыль(коп.)
        /// </summary>
        public int price { get; set; } = 1000;

        /// <summary>
        /// Скидка
        /// </summary>
        public double discount { get; set; } = 10;

        /// <summary>
        /// таймер между вещами(мс)
        /// </summary>
        public int Itimer { get; set; } = 300;

        /// <summary>
        /// Время между обновлением цен предметов(мин.)
        /// </summary>
        public double UpdatePriceTimerTime { get; set; } = 12;

        /// <summary>
		/// Время между отсылкой уведомлений сайту(мин.)
		/// </summary>
        public double PingPongTimerTime { get; set; } = 0.5;

        public Dictionary<MessageType, bool> Messages = new Dictionary<MessageType, bool>();

        /// <summary>
        /// Массовое обновление цен инвентаря
        /// </summary>
        public bool MassUpdate { get; set; } = false;

        public static void Reload()
        {
            if (File.Exists("config.json"))
                m_config = JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));
            else
                m_config = new Config();
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
    }
}
