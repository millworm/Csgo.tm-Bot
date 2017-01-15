using System;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using Newtonsoft.Json;
using System.Runtime.Serialization.Formatters.Binary;
using WebSocketSharp;
using System.Timers;

using CSharpTradeOffers.Configuration;
using CSharpTradeOffers.Trading;
using CSharpTradeOffers.Web;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MonoTM2
{
    class Program
    {
        static void Main()
        {
            CBot bot = new CBot();
            //загрузка всех настроек
            bot.Loader();
            //стартуем
            bot.Start();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Запуск выполнен");
            Console.ResetColor();
            string Mess = "";
            while (Mess != "q")
            {
                Mess = Console.ReadLine();
                try
                {
                    switch (Mess.ToLower())
                    {
						//показывать какие предметы были выставлены из списка покупок
                        case "w":
                            if (bot.exhibitMessage)
                                bot.exhibitMessage = false;
                            else
                                bot.exhibitMessage = true;
                            Console.WriteLine(bot.exhibitMessage.ToString());
                            break;
						//подтвердить трейды
                        case "t":
                            bot.ConfirmTrade();
                            break;
						//добавить предмет
                        case "add":
                        case "a":
                            Console.WriteLine("Введите ссылку");
                            var link = Console.ReadLine();
                            bot.AddItem(link);
                            break;
						//обноление
                        case "update":
                            //обноление цен
                            bot.UpdatePrice();
                            //проверка быстрых покупок
                            bot.CheckQuickOrders();
                            break;
						//вывести прибыль с предмета
                        case "gprice":
                            Console.WriteLine(bot.GetPrice());
                            break;
                        case "sprice":
                            Console.WriteLine("Введите прибыль");
                            bot.SetPrice(Console.ReadLine());
                            Console.WriteLine("Новая прибыль " + bot.GetPrice().ToString());

                            break;
						//вывести список предметов	
                        case "list":
                            Console.ForegroundColor = ConsoleColor.Magenta;
                            int n = 0;
                            var tempItems = bot.GetListItems();
                            foreach (var y in tempItems)
                                Console.WriteLine("[{3}] {0} {1} {2}", y.name, y.price, y.count, n++);
                            Console.ResetColor();
                            break;
                        
						//удалить предмет
                        case "delete":
                        case "d":
                            Console.WriteLine("Введите номер");
                            var N = Convert.ToInt16(Console.ReadLine());
                            Console.WriteLine("Удалить {0}  ?", bot.GetItem(N).name);
                            var A = Console.ReadLine().ToLower();
                            if (A == "y")
                                bot.RemoveItem(N);
                            Console.WriteLine("Удалено");                      
                            break;
                        case "stime":
                            Console.WriteLine("Введите время");
                            bot.SetUpdateTimer(Console.ReadLine());
                            Console.WriteLine("Новое время " + bot.GetUpdateTimer().ToString());
                            break;
                        //case "trade":
                        //    if (isPolling)
                        //    {
                        //        Console.WriteLine("Отключить автоприем вещей?");
                        //        var tmes = Console.ReadLine();
                        //        if (tmes.ToLower() == "y")
                        //        {
                        //            Console.WriteLine("Отключено");
                        //            isPolling = false;
                        //        }
                        //    }
                        //    else
                        //    {
                        //        Console.WriteLine("Включить автоприем вещей?");
                        //        var tmes = Console.ReadLine();
                        //        if (tmes.ToLower() == "y")
                        //        {
                        //            isPolling = true;
                        //            Trades();
                        //        }

                        //    }
                        //    break;
                    }
                }
                catch (Exception tty)
                {
                    Console.WriteLine(tty.Message);
                }
            }
        }

    }
    class CBot
    {
      public  bool exhibitMessage = false;
        //переменные для подтверждений трейдов 
        private string _user, _pass;
        private string _apiKey;
        private Account _account;
        private DefaultConfig _config = new DefaultConfig("configuration.json");
        private readonly JsonConfigHandler ConfigHandler = new JsonConfigHandler();
        private readonly Web Web = new Web(new SteamWebRequestHandler());

        EconServiceHandler offerHandler;
        MarketHandler marketHandler;
        bool autoConfirmTrades = false;
        //конфиг
        Config cfg = new Config();
        //
        delegate void Accept();
        delegate void FastPrice();
        delegate void UpdatePriceD();

        functions CLIENT, CLIENT1;
        private WebSocket client;
        const string host = "wss://wsn.dota2.net/wsn/";
        public List<Itm> Items = new List<Itm>();
        string key = "";
        Task Finding;
        UpdatePriceD UpdatePriceDelegate = null;
        FastPrice QuickOrder = null;
        Accept AcceptTrade = null;

        System.Timers.Timer tmr = new System.Timers.Timer();
        System.Timers.Timer tmr2 = new System.Timers.Timer();

        void Trades()
        {
            autoConfirmTrades = true;
            //загрузка данный для подтверждений трейдов
            _config = ConfigHandler.Reload(_config);
            _apiKey = _config.ApiKey;
            _user = _config.Username;
            _pass = _config.Password;
            _account = Web.RetryDoLogin(TimeSpan.FromSeconds(5), 10, _user, _pass, _config.SteamMachineAuth);

            if (!string.IsNullOrEmpty(_account.SteamMachineAuth))
            {
                _config.SteamMachineAuth = _account.SteamMachineAuth;
                ConfigHandler.WriteChanges(_config);
            }
            Console.WriteLine("Login was successful!");

            offerHandler = new EconServiceHandler(_apiKey);
            marketHandler = new MarketHandler();
            marketHandler.EligibilityCheck(_account.SteamId, _account.AuthContainer);

            //
            AcceptTrade = AcceptTrades;
        }

       public void Loader()
        {
           //таймер обновления цен
            tmr.Elapsed += new ElapsedEventHandler(PriceCorrectTimer);
            tmr.Interval = cfg.UpdatePriceTimerTime * 60 * 1000;

            tmr2.Elapsed += new ElapsedEventHandler(PingPongTimer);
            tmr2.Interval = cfg.PingPongTimerTime * 60 * 1000;

            //Console.WriteLine("Автоматически получать вещи?");
            //var w = Console.ReadLine();
            //if (w.ToLower() == "y")
            //{
            //    Trades();
            //}
            UpdatePriceDelegate = CorrectPrice;
            QuickOrder = QuickOrderF;


            CLIENT = new functions();
            CLIENT1 = new functions();

           //загрузка итемов
            FileInfo b = new FileInfo("csgotmitems.dat");
            if (b.Exists)
            {
                FileStream fstream = File.Open("csgotmitems.dat", FileMode.Open);
                BinaryFormatter binaryFormatter = new BinaryFormatter();
                Items = (List<Itm>)binaryFormatter.Deserialize(fstream);
                fstream.Close();
                Items.Sort(delegate(Itm i1, Itm i2) { return i1.name.CompareTo(i2.name); });
            }

            //загрузка настроек
            if (!File.Exists("config.json"))
            {
                cfg.price = 1000;
                cfg.key = key;
                cfg.discount = 10;
                Config.Reload(cfg);
            }
            else
            {
                cfg = Config.Reload(cfg);

            }


            client = new WebSocket(host);

            string id = "", answer = "", answer1 = "";

            client.OnOpen += (ss, ee) =>
                Console.WriteLine(string.Format("Подключение к {0} успешно ", host));

            client.OnError += (ss, ee) =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(string.Format(string.Format("Ошибка: {0}\n{1}", ee.Message, ee.Exception) + Environment.NewLine));
                Console.ResetColor();
            };
            var product = new { type = "" };
            var invcache_go = new { data = new { time = "" } };
            var webnotify = new { data = new { text = "" } };
            var itemstatus4 = new { data = new { bid = "" } };
            var itemstatus = new { data = new { status = "" } };
            var itemout = new { data = new { ui_status = "" } };

            client.OnMessage += (ss, ee) =>
            {
                var type = JsonConvert.DeserializeAnonymousType(ee.Data, product);
                try
                {
                    switch (type.type)
                    {
                        case "webnotify":

                            break;

                        case "itemout_new_go":
                        case "itemstatus_go":
                            answer = ee.Data;
                            answer = answer.Replace(":\"{", ":{");
                            answer = answer.Replace("}\"}", "}}");
                            answer = answer.Replace(@"\", "");
                            var ans1 = JsonConvert.DeserializeObject<TrOf>(answer);
                            switch (ans1.data.status)
                            {
                                case 2:
                                    Console.ForegroundColor = ConsoleColor.Magenta;
                                    Console.WriteLine("[{0}] Передать вещи", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00"));
                                    Console.ResetColor();
                                    break;
                                case 4:
                                    if (autoConfirmTrades)
                                    {
                                        AcceptTrade.BeginInvoke(null, null);
                                    }
                                    Console.ForegroundColor = ConsoleColor.Cyan;
                                    Console.WriteLine("[{0}] Забрать вещи", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00"));
                                    Console.ResetColor();
                                    break;

                            }


                            break;

                        case "additem_go":
                        case "invcache_go":
                        case "invcache_gt":
                        case "invcache_cs":
                        case "invcache_tf":
                        case "onlinecheck":
                        case "setonline":
                        case "money":
                        case "webnotify_betsonly":
                            break;
                        case "newitems_go":
                            answer = ee.Data;
                            answer = ee.Data.Replace("\\\"", "\"");
                            answer = answer.Replace(":\"{", ":{");
                            answer = answer.Replace("}\"}", "}}");
                            answer = answer.Replace("\\\"\\\\u", "").Replace("\\\"\",", "\\\",");
                            WSItm t = JsonConvert.DeserializeObject<WSItm>(answer);

                            id = t.data.i_classid + "_" + t.data.i_instanceid;
                            var ItemInList = Items.Find(item => item.id == id && item.turn); //&& item.price >= (t.data.ui_price));

                            if (ItemInList != null && (ItemInList.price >= (t.data.ui_price)))
                            {
                                if (CLIENT1.Buy(ItemInList, cfg.key))
                                {
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    new System.Action(() => Console.WriteLine(string.Format("[{3}] {0} куплен за {1} ({2})", ItemInList.name, t.data.ui_price, ItemInList.price, "сокеты"))).Invoke();
                                    Console.ResetColor();
                                }
                                else
                                {
                                    new System.Action(() => Console.WriteLine(string.Format("Не успел купить {0} выставлен за {1} ({2})", ItemInList.name, t.data.ui_price, ItemInList.price))).BeginInvoke(null, null);
                                }

                                
                            }
                            if (exhibitMessage && ItemInList!=null)
                            {
                                new System.Action(() => Console.WriteLine(string.Format("{0} выставлен за {1} ({2})", ItemInList.name, t.data.ui_price, ItemInList.price))).BeginInvoke(null,null);
                                Thread.Sleep(0);
                            }

                            break;
                        case "history_go":
                            /*answer = ee.Data.Replace(@"\", ""); ;
                            System.Text.RegularExpressions.Regex RID = new System.Text.RegularExpressions.Regex(@"\d+-\d+");
                            System.Text.RegularExpressions.Regex RPrice = new System.Text.RegularExpressions.Regex(@"\d+\.\d+");
                            if (RID.IsMatch(answer))
                            {
                                var Iid=RID.Match(answer);
                                var IPrice = RPrice.Match(answer);
                                var itm=Items.Find(item => item.id == Iid.Value);
                                if (itm!=null && Convert.ToDouble(IPrice)>itm.price)
                                {
                                    CorrectPrice(itm);
                                }
                            }*/
                            break;
                        default:
                            Console.WriteLine(string.Format("Сообщение: \n\r{0}", ee.Data));
                            break;

                    }



                }
                catch (Exception e)
                {
                    if (type.type != "newitems_go")
                        Console.WriteLine("Error " + type.type + " " + e.Message + Environment.NewLine + Environment.NewLine + answer1 + Environment.NewLine + Environment.NewLine + answer + Environment.NewLine + Environment.NewLine);
                }

            };

            client.OnClose += (ss, ee) =>
            {
                Console.WriteLine(string.Format("Отключен от {0}", host));
                string wskey = CLIENT.GetWS(cfg.key);
                WS t = JsonConvert.DeserializeObject<WS>(wskey);

                client.Connect();
                if (t.error == null && t.wsAuth != null)
                    client.Send(t.wsAuth);
                else
                    Console.WriteLine(t.error);
                client.Send("newitems_go");
            };



        }
        /// <summary>
        /// Поток проходящий вещи
        /// </summary>
        // public void find()
        async Task find()
        {
            while (true)
            {
                for (int i = 0; i < Items.Count; i++)
                {
                    {
                        if (CLIENT.Buy(Items[i], cfg.key))
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            new System.Action(() => Console.WriteLine(string.Format("[{2}] find {0} куплен. Цена на покупку ({1})", Items[i].name, Items[i].price, DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00")))).BeginInvoke(null, null);
                            Console.ResetColor();
                        }
                        //Thread.Sleep(cfg.Itimer);
                        await Task.Delay(cfg.Itimer);
                    }
                }
                // Thread.Sleep(0);
                await Task.Delay(0);
            }
        }

        /// <summary>
        /// Запуск покупок. Выставляем цены, проверяем быстрые покупки, включаем обычный поиск и сокеты
        /// </summary>
        public void Start()
        {

            CorrectPrice();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Цены скорректированы");
            Console.ResetColor();
            //проверка быстрых покупок
            QuickOrder.BeginInvoke(null, null);
            //запуск поиска
            if (Items.Count > 0)
            {
                // Find.Start();
                Finding = find();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Поиск включен");
                Console.ResetColor();
            }
            //запуск сокетов
            string wskey = CLIENT.GetWS(cfg.key);
            WS t = JsonConvert.DeserializeObject<WS>(wskey);
            client.Connect();
            if (t.error == null && t.wsAuth != null)
                client.Send(t.wsAuth);
            else
                Console.WriteLine(t.error);
            Thread.Sleep(0);


            client.Send("newitems_go");

            //запуск таймеров
            tmr.Enabled = true;
            tmr2.Enabled = true;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Таймеры включены");
            Console.ResetColor();

        }

        /// <summary>
        /// Обновление цен
        /// </summary>
        void CorrectPrice()
        {
            try
            {
                double Averange, MinPrice, Discount;
                Discount = 1 - Convert.ToDouble(cfg.discount / 100);
                int count = 0;
                foreach (Itm I in Items)
                {
                    Thread.Sleep(cfg.Itimer);
                    new System.Action(() => Console.Write("\r{0}/{1} ", ++count, Items.Count)).BeginInvoke(null, null);
                    MinPrice = Convert.ToDouble(CLIENT.GetMinPrice(I, cfg.key));
                    if (MinPrice == -1) continue;

                    Averange = Convert.ToDouble(CLIENT.GetAverangePrice(I, cfg.key));
                    I.count = 1;
                    if (Averange < MinPrice)
                    {
                        if (Averange > 20000)
                        {
                            I.price = Math.Round((Averange * Discount - cfg.price * 2) / 100.0, 2);
                            continue;
                        }
                        if (Averange > 10000)
                        {
                            I.price = Math.Round((Averange * Discount - cfg.price) / 100.0, 2);
                            continue;
                        }
                        else
                        {
                            I.price = Math.Round((Averange * Discount - cfg.price) / 100.0, 2);
                            if (I.price < 0.50)
                            {
                                I.price = 0.0;
                                I.turn = false;
                                I.count = 0;
                            }
                            continue;
                        }
                    }
                    else
                    {
                        if (MinPrice > 20000)
                        {
                            I.price = Math.Round((MinPrice * Discount - cfg.price * 2) / 100.0, 2);
                            continue;
                        }
                        if (MinPrice > 10000)
                        {
                            I.price = Math.Round((MinPrice * Discount - cfg.price) / 100.0, 2);
                            continue;
                        }
                        else
                        {
                            I.price = Math.Round((MinPrice * Discount - cfg.price) / 100.0, 2);
                            if (I.price < 0.5)
                            {
                                I.price = 0.0;
                                I.turn = false;
                                I.count = 0;
                            }
                            continue;
                        }
                    }



                }
                Console.WriteLine(string.Format("Обновлены цены {0}:{1}", DateTime.Now.Hour.ToString("00"), DateTime.Now.Minute.ToString("00")));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[CorrectPrice] {0}", ex.Message);
                Console.ResetColor();
            }


        }

        /// <summary>
        /// быстрые покупки
        /// </summary>
        void QuickOrderF()
        {
            int n = 0; // число вещей, найденых из нашего списка на покупку
            //получаем список вещей
            var QuickItemsList = CLIENT.QList(cfg.key);
            //проходимся по нашему списку элементов
            foreach (var tempItem in Items)
            {
                //ищем их быстрой покупке
                var findItem = QuickItemsList.Find(item => item.i_classid + "_" + item.i_instanceid == tempItem.id);
                //если нашли, то проверяем цену
                if (findItem != null)
                {
                    n++;
                    if (Convert.ToDouble(findItem.l_paid) < tempItem.price * 100)
                    {
                        //покупаем
                        if (CLIENT.QBuy(cfg.key, findItem.ui_id))
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine(string.Format("[{4}] {3} {0} куплен за {1} ({2})", tempItem.name, Convert.ToDouble(findItem.l_paid) / 100, tempItem.price, "Список", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00")));
                            Console.ResetColor();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Сохраняет настройки и итемы в файл
        /// </summary>
        /// <param name="S">true сохраняет настройки. false сохраняет итемы</param>
        void Save(bool S)
        {
            //сохраняем настройки
            if (S)
            {
                Config.Save(cfg);
            }
            else
            {
                //сохраняем итемы
                FileStream fstream = File.Open("csgotmitems.dat", FileMode.Create);
                BinaryFormatter binaryFormatter = new BinaryFormatter();
                binaryFormatter.Serialize(fstream, Items);
                fstream.Close();
                Console.WriteLine("Список сохранен");
            }
        }

        /// <summary>
        /// Обновление цен и проверка обменов
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void PriceCorrectTimer(object sender, ElapsedEventArgs e)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("[{0}] таймер", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00"));
            Console.ResetColor();
            //выставитьToolStripMenuItem_Click(sender, e);
            UpdatePriceDelegate.BeginInvoke(null, null);

            if (autoConfirmTrades)
            {
                var trades = CLIENT1.GetTrades(cfg.key);
                foreach (var tr in trades.items)
                {
                    if (tr.ui_status == "4")
                        AcceptTrade.BeginInvoke(null, null);
                }
            }

        }

        /// <summary>
        /// Говорим сайту что мы онлайн и проверям быстрые покупки
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void PingPongTimer(object sender, ElapsedEventArgs e)
        {
            /* Console.ForegroundColor = ConsoleColor.DarkMagenta;
             Console.WriteLine("[{0}] пинг", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00"));
             Console.ResetColor();*/
            //выставитьToolStripMenuItem_Click(sender, e);
            CLIENT.Ping(cfg.key);
            //
            client.Send("PING");
            //проверка быстрых покупок
            QuickOrder.BeginInvoke(null, null);

        }

        /// <summary>
        /// Функция подтверждение трейдов
        /// </summary>
        void AcceptTrades()
        {

            try
            {
                var tr = CLIENT1.MarketTrades(cfg.key);
                if (tr != null && tr.trades[0].dir == "out")
                {
                    var ans = offerHandler.AcceptTradeOffer(tr.trades[0].trade_id, tr.trades[0].bot_id, _account.AuthContainer, "1");
                    if (ans != "false")
                    {
                        Console.WriteLine("Обмен принят!");
                        CLIENT1.UpdateInvent(cfg.key);
                    }
                    else
                        Console.WriteLine("Обмен не принят");
                }
                else
                {

                    int b = 0, of = 0;
                    Console.WriteLine("Определяем бота");
                bot:
                    var bid = CLIENT.GetBotID(cfg.key);
                    if (bid != "0")
                    {
                        Console.WriteLine("Получаем трейд");
                    offer:
                        var offer = CLIENT.GetOffer(bid, cfg.key);
                        if (offer != null)
                        {

                            Console.WriteLine("Подтверждаем");
                            var botsteamid = offer.profile.Substring(offer.profile.IndexOf("s/") + 2).Replace("/", "");
                            var ans = offerHandler.AcceptTradeOffer(offer.trade, botsteamid, _account.AuthContainer, "1");
                            if (ans != "false")
                            {
                                Console.WriteLine("Обмен принят!");
                                CLIENT1.UpdateInvent(cfg.key);
                                return;
                            }
                            else
                                Console.WriteLine("Обмен не принят");

                        }
                        else
                        {
                            Console.WriteLine("Не дает оффер");
                            if (of++ < 2) goto offer;
                        }
                    }
                    else
                    {
                        if (b++ < 2) goto bot;
                    }
                }
            }

            catch (Exception exx)
            {
                Console.WriteLine(exx.GetBaseException() + "\n\n" + exx.InnerException + "\n\n" + exx.Source + "\n\n" + exx.StackTrace + "\n\n" + exx.TargetSite);

            }
        }

        /// <summary>
        /// подтвердить трейд
        /// </summary>
        public void ConfirmTrade()
        {
            AcceptTrade.Invoke();
        }

        /// <summary>
        /// Добавление предмета в список покупок
        /// </summary>
        /// <param name="link">ссылка на страницу с предметом</param>
       public void AddItem(string link)
        {
            
            var itm = new Itm { link = link, count = 1, price = 0, turn = true };

            itm.hash = CLIENT.GetHash(itm, cfg.key);
            //проверяем, нет ли этого предмета в списке
            var ch = Items.Find(p => p.id == itm.id);
            if (ch != null)
            {
                Console.WriteLine("Данный предмет уже есть");
                return;
            }


            Console.WriteLine("Добавлен");
            Items.Add(itm);
            Save(false);
        }

        /// <summary>
        /// Обновление цен на предметы
        /// </summary>
       public void UpdatePrice()
       {
           UpdatePriceDelegate.Invoke();
       }
        /// <summary>
        /// Проверка быстрых покупок
        /// </summary>
       public void CheckQuickOrders()
       {
           QuickOrder.Invoke();
       }
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
       public int GetPrice()
       {
           return cfg.price;
       }
        /// <summary>
        /// Установить прибыль
        /// </summary>
        /// <param name="NewPrice">Прибыль в копейках</param>
       public void SetPrice(string NewPrice)
       {
           try
           {
               cfg.price = Convert.ToInt16(NewPrice);
               Save(true);
           }
           catch(Exception ex){
               Console.WriteLine("Ошибка в выставлении прибыли/r/nМетод {0}/r/nОшибка:{1}", System.Reflection.MethodInfo.GetCurrentMethod().Name, ex.Message);
           }
       }

       public List<Itm> GetListItems()
       {
           return Items;
       }

       public Itm GetItem(int index)
       {
           return Items[index];
       }

       public void RemoveItem(int index)
       {
           Items.RemoveAt(index);
           Save(false);
       }


      public void SetUpdateTimer(string time)
       {
           cfg.UpdatePriceTimerTime = Convert.ToInt16(time);
       }

      public double GetUpdateTimer()
       {
           return cfg.UpdatePriceTimerTime;
       }
    }
}
