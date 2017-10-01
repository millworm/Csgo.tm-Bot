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
        //ключ
        public string key { get; set; } = "";
        //прибыль
        public int price { get; set; } = 1000;
        //скидка
        public double discount { get; set; } = 10;
        //таймер между вещами
        public int Itimer { get; set; } = 300;
        //таймер между обновлениями цен
        public double UpdatePriceTimerTime { get; set; } = 12;
        //таймер пинпонга
        public double PingPongTimerTime { get; set; } = 0.5;

        public Dictionary<MessageType, bool> Messages = new Dictionary<MessageType, bool>();
        //public Config()
        //{
        //    InitializeAll();
        //}

        //public void InitializeAll()
        //{
        //    key = "";
        //    price = 1000;
        //    discount = 10;
        //    Itimer = 300;
        //    UpdatePriceTimerTime = 12;
        //    PingPongTimerTime = 0.5;
        //}

        public static Config Reload(){
                        return JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));
        }

        public static void Save(Config cfg)
        {
            File.WriteAllText("config.json", JsonConvert.SerializeObject(cfg));
        }

    }
}
