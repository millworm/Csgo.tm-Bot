using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonoTM2.Classes
{
   public class SteamSettings
    {
        public string Login { get; set; } = "";
        public string Password { get; set; } = "";
        public string MachineAuth { get; set; } = "";
        public string ApiKey { get; set; } = "";

        /// <summary>
        /// Таймаут переавторизации в стиме
        /// Чтобы не словить бан за частые перезаходы
        /// </summary>
        public int TimeoutRelogin { get; set; } = 180;
    }
}
