using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
namespace MonoTM2
{
    [JsonObject(Title = "RootObject")]
    class Config
    {
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

        public static Config Reload(){
                        return JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));
        }

        public static void Save(Config cfg)
        {
            File.WriteAllText("config.json", JsonConvert.SerializeObject(cfg));
        }

    }
}
