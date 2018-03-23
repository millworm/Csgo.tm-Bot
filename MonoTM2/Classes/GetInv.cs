using System.Collections.Generic;
using Newtonsoft.Json;

namespace MonoTM2.Classes
{
    public class GetInv
    {
        [JsonProperty("ok")]
        public bool success { get; set; }

        [JsonProperty("data")]
        public List<GetInvDatum> data { get; set; }

        [JsonProperty("error")]
        public string error { get; set; }
    }

    public class GetInvDatum
    {
        public string ui_id { get; set; }
        public string i_market_hash_name { get; set; }
        public string i_market_name { get; set; }
        public string i_name { get; set; }
        public string i_name_color { get; set; }
        public string i_rarity { get; set; }
        public List<IDescription> i_descriptions { get; set; }
        public int ui_status { get; set; }
        public string he_name { get; set; }
        public int ui_price { get; set; }
        public int min_price { get; set; }
        public bool ui_price_text { get; set; }
        public bool min_price_text { get; set; }
        public string i_classid { get; set; }
        public string i_instanceid { get; set; }
        public bool ui_new { get; set; }
        public int position { get; set; }
        public string wear { get; set; }
        public int tradable { get; set; }
        public double i_market_price { get; set; }
        public string i_market_price_text { get; set; }
    }
}
