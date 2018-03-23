using System.Collections.Generic;
using Newtonsoft.Json;

namespace MonoTM2.Classes
{
    public class MassInfo
    {
        [JsonProperty("success")]
        public bool success { get; set; }

        [JsonProperty("results")]
        public List<MassInfoResult> results { get; set; }

        [JsonProperty("error")]
        public string error { get; set; }
    }

    public class MassInfoResult
    {
        [JsonProperty("classid")]
        public string classid { get; set; }

        [JsonProperty("instanceid")]
        public string instanceid { get; set; }

        [JsonProperty("sell_offers")]
        public MassInfoOffers sellOffers { get; set; }

        [JsonProperty("history")]
        public MassInfoHistory history { get; set; }

        [JsonProperty("buy_offers")]
        public MassInfoOffers buyOffers { get; set; }

        [JsonProperty("info")]
        public Info info { get; set; }
    }

    public class MassInfoHistory
    {
        [JsonProperty("max")]
        public uint max { get; set; }

        [JsonProperty("average")]
        public uint average { get; set; }

        [JsonProperty("min")]
        public uint min { get; set; }

        [JsonProperty("number")]
        public uint number { get; set; }

        [JsonProperty("history")]
        public List<List<long>> otherHistory { get; set; }

    }

    public class MassInfoOffers
    {
        [JsonProperty("best_offer")]
        public int bestOffer { get; set; }

    }

    public class MassInfoInfo
    {
        [JsonProperty("name")]
        public string name { get; set; }

        [JsonProperty("market_name")]
        public string marketName { get; set; }

        [JsonProperty("market_hash_name")]
        public string marketHashName { get; set; }

        [JsonProperty("mtype")]
        public string mtype { get; set; }

        [JsonProperty("quality")]
        public string quality { get; set; }

        [JsonProperty("slot")]
        public string slot { get; set; }

        [JsonProperty("our_market_instanceid")]
        public object ourMarketInstanceid { get; set; }

        [JsonProperty("rarity")]
        public string rarity { get; set; }

        [JsonProperty("type")]
        public string type { get; set; }
    }
}
