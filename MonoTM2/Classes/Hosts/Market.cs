using System;
using Newtonsoft.Json;
namespace MonoTM2.Classes.Hosts
{
   public class Market
    {
        public double Discount { get; set; } = 10;
        public bool Enable { get; set; } = false;
        /// <summary>
        /// Прибыль c вещей
        /// </summary>
        public int Profit { get; set; } = 1000;

        /// <summary>
        /// Время между обновлением цен предметов(мин.)
        /// </summary>
        public double UpdatePriceTimerTime { get; set; } = 12;

        /// <summary>
        /// Массовое обновление цен инвентаря
        /// </summary>
      //  public bool MassUpdate { get; set; } = true;
    }
}
