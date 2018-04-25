using Newtonsoft.Json;

namespace MonoTM2.Classes
{
    public class InventoryItems
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("updatingNow")]
        public bool UpdatingNow { get; set; }

        [JsonProperty("lastUpdate")]
        public int LastUpdate { get; set; }

        [JsonProperty("itemsInCache")]
        public int ItemsInCache { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }
}
