using System;
using Newtonsoft.Json;
using System.IO;
namespace MonoTM2
{
    [JsonObject(Title = "RootObject")]
    [Serializable]
    class Config
    {
        //ключ
        public string key { get; set; } 
        //прибыль
        public int price { get; set; } 
        //скидка
        public double discount { get; set; } 
        //таймер между вещами
        public int Itimer { get; set; }
        //таймер между обновлениями цен
        public double UpdatePriceTimerTime { get; set; }
        //таймер пинпонга
        public double PingPongTimerTime { get; set; }

        public Config()
        {
            InitializeAll();
        }

        public void InitializeAll()
        {
            key = "";
            price = 1000;
            discount = 10;
            Itimer = 300;
            UpdatePriceTimerTime = 12;
            PingPongTimerTime = 0.5;
        }

        public static Config Reload(Config cfg){

            if (!File.Exists("config.json"))
            {
                File.WriteAllText("config.json", JsonConvert.SerializeObject(cfg));
            }

            cfg = JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));

            return cfg;
        }

        public static void Save(Config cfg)
        {
            File.WriteAllText("config.json", JsonConvert.SerializeObject(cfg));
        }
    }
}
