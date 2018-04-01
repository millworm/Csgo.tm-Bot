namespace MonoTM2.Const
{
    public static class Host
    {
        public  const string CSGO = "https://market.csgo.com";
        public  const string PUBG = "https://pubg.tm";
        public  const string DOTA2 = "https://market.dota2.net";

        public static string FindHost(string str)
        {
            if (CSGO.Contains(str))
                return CSGO;
            if (PUBG.Contains(str))
                return PUBG;
            if (DOTA2.Contains(str))
                return DOTA2;
            return null;
        }
    }


}
