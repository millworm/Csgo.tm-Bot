
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using WebSocketSharp;
using SteamToolkit.Configuration;
using SteamToolkit.Trading;
using SteamToolkit.Web;
using SteamAuth;
using LoginResult = SteamAuth.LoginResult;

namespace MonoTM2
{
    public class CBot
    {
        private ReaderWriterLockSlim listLock = new ReaderWriterLockSlim();

        //переменные для подтверждений трейдов 
        private string _user, _pass;
        private string _apiKey;
        private Account _account;
        private DefaultConfig _config = new DefaultConfig("configuration.json");
        private readonly JsonConfigHandler ConfigHandler = new JsonConfigHandler();

        private readonly Web Web = new Web(new SteamWebRequestHandler());
        EconServiceHandler offerHandler;
        MarketHandler marketHandler;


        bool autoConfirmTrades, Close, accountFileExist;
        bool tradesOut = true, tradesIn = true;
        //конфиг
        Config cfg = new Config();
        //
        //delegate void voidDelegate();

        Thread Find;
        Task Finding;

        functions CLIENT, CLIENT1;
        private WebSocket client;
        
        const string host = "wss://wsn.dota2.net/wsn/";
        List<Itm> Items = new List<Itm>();

        System.Action AcceptMobileOrdersAction, UpdatePriceDelegateAction, QuickOrderAction,
            AcceptTradeAction, UpdateOrdersDelegate, UpdateNotificationsDelegate, CheckNotificationsDelegate;

        AsyncCallback CallbackUpdater;
        IAsyncResult IAcceptOutTradesResult, IAcceptInTradesResult;

        System.Timers.Timer UpdatePriceTimer = new System.Timers.Timer();
        System.Timers.Timer pinPongTimer = new System.Timers.Timer();

        void Trades()
        {

            autoConfirmTrades = true;

            try
            {
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
                Console.WriteLine("Авторизован!");

                offerHandler = new EconServiceHandler(_apiKey);
                marketHandler = new MarketHandler();
                marketHandler.EligibilityCheck(_account.SteamId, _account.AuthContainer);

                //
            }
            catch (Exception ex)
            {
                autoConfirmTrades = false;
                Console.WriteLine(ex.Message);
            }


        }

        public void Loader()
        {
           
            AcceptTradeAction = AcceptTrades;

            if (!String.IsNullOrEmpty(_config.Username))
            {
                //если есть мобильный аутентификатор включаем автоматический прием вещей
                //если его нет, то узнаем, нужно ли забирать вещи самостоятельно 
                if (File.Exists("account.maFile"))
                {
                    accountFileExist = true;
                    Trades();
                }
                else
                {
                    Console.WriteLine(("Автоматически получать вещи?"));
                    var w = Console.ReadLine();
                    if (w.ToLower() == "y")
                    {
                        Trades();
                    }
                }
            }
            else
            {
                Console.WriteLine("Заполните configuration.json для автоматического получения вещей");
            }


            CallbackUpdater = new AsyncCallback(CorrectOrdersAndNotifications);

            UpdatePriceDelegateAction = CorrectPrice;
            UpdateOrdersDelegate = CorrectOrders;
            UpdateNotificationsDelegate = CorrectNotifications;
            QuickOrderAction = QuickOrderF;
            AcceptMobileOrdersAction = AcceptMobile;
            CheckNotificationsDelegate = CheckNotifications;

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
                Items.Sort(delegate (Itm i1, Itm i2) { return i1.name.CompareTo(i2.name); });
            }

            //загрузка настроек
            if (!File.Exists("config.json"))
            {
                cfg.price = 1000;
                cfg.key = "";
                cfg.discount = 10;

                var mm = Enum.GetValues(typeof(MessageType));
                foreach (var it in mm)
                {
                    cfg.Messages.Add((MessageType)it, true);
                }

                Config.Reload(cfg);
            }
            else
            {
                cfg = Config.Reload(cfg);

            }

            if (cfg.Messages.Count == 0 || cfg.Messages.Count != Enum.GetNames(typeof(MessageType)).Length)
            {
                var mm = Enum.GetValues(typeof(MessageType));
                foreach (var it in mm)
                {
                    try
                    {
                        cfg.Messages.Add((MessageType)it, true);
                    }
                    catch { }
                }

                Config.Save(cfg);

            }



            //таймер обновления цен
            UpdatePriceTimer.Elapsed += new ElapsedEventHandler(PriceCorrectTimer);
            UpdatePriceTimer.Interval = cfg.UpdatePriceTimerTime * 60 * 1000;

            pinPongTimer.Elapsed += new ElapsedEventHandler(PingPongTimer);
            pinPongTimer.Interval = cfg.PingPongTimerTime * 60 * 1000;

            client = new WebSocket(host);

            string id = "", answer = "", answer1 = "";

            client.OnOpen += (ss, ee) =>
                    WriteMessage(string.Format("Подключение к {0} успешно ", host), MessageType.Info);

            client.OnError += (ss, ee) =>
            {
                //WriteMessage(string.Format("Ошибка: {0}\n{1}" + Environment.NewLine, ee.Message, ee.Exception), MessageType.Error);
            };

            var product = new { type = "" };
            var invcache_go = new { data = new { time = "" } };
            var webnotify = new { data = new { classid = "", instanceid = "", price = 0.0, way = "" } };
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

                            /* answer = ee.Data.Replace("\\\"\",", "\\\",").Replace(@"\", "").Replace(" \"u0", "");
                             answer = answer.Replace(":\"{", ":{").Replace("\"\"", "");
                             answer = answer.Replace("}\"}", "}}");
                             answer = answer.Replace("\\\"\",", "\\\",");*/
                            answer = ee.Data.Replace("\"\\\\u", "");
                            answer = answer.Replace("\\\\\\", "\\");
                            answer = answer.Replace("\\\"", "\"");
                            answer = answer.Replace(@"\""", "\"");
                            answer = answer.Replace(":\"\"{", ":{");
                            answer = answer.Replace("\"\",", "\",");
                            answer = answer.Replace("}\"\"}", "}}");

                            var ansWeb = JsonConvert.DeserializeAnonymousType(answer, webnotify);
                            //if (ansWeb != null && ansWeb.data.way == null)
                            //{
                            id = ansWeb.data.classid + "_" + ansWeb.data.instanceid;
                            var ItemInListWeb = Items.Find(item => item.id == id); //&& item.price >= (t.data.ui_price));

                            if (ItemInListWeb != null && (CLIENT1.Buy(ItemInListWeb, cfg.key) || CLIENT1.Buy(ItemInListWeb, cfg.key, 100)))
                            {
                                WriteMessage(string.Format("*webnotify* {0} куплен за {1} ({2})", ItemInListWeb.name, ansWeb.data.price, ItemInListWeb.price), MessageType.BuyWeapon);
                            }
                            else
                            {
                                WriteMessage(string.Format("*webnotify* Не успел купить {0} выставлен за {1} ({2})", ItemInListWeb.name, ansWeb.data.price, ItemInListWeb.price), MessageType.Socket);
                            }

                            //}
                            break;
                        case "itemstatus_go":
                            //{"type":"itemstatus_go","data":"{\"id\":\"173474064\",\"status\":5}"}
                            //WriteMessage("itemstatus_go " + ee.Data);
                            answer = ee.Data.Replace("\\\"", "\"");
                            answer = answer.Replace(":\"{", ":{");
                            answer = answer.Replace("}\"}", "}}");
                            var itemstatus_go = new { data = new { status = "" } };
                            var ansItem = JsonConvert.DeserializeObject<TrOf>(answer);
                            switch (ansItem.data.status)
                            {
                                case 4:
                                    WriteMessage("Забрать вещи", MessageType.GetWeapon);
                                    if (autoConfirmTrades)
                                    {
                                        AcceptTradeAction.Invoke();
                                    }
                                    //AcceptTrade.BeginInvoke(null,null);
                                    break;

                            }
                            break;
                        case "itemout_new_go":

#if DEBUG
                            System.Diagnostics.Debug.WriteLine(ee.Data);

#endif
                            //WriteMessage("itemout_new_go "+ee.Data);
                            answer = ee.Data.Replace("\\\"", "\"");
                            answer = answer.Replace(":\"{", ":{");
                            answer = answer.Replace("}\"}", "}}");
                            //answer = answer.Replace(":\"\"{", ":{");
                            //answer = answer.Replace("}\"\"}", "}}");
                            answer = answer.Replace(@"\", "");
                            var TemplItemout_new_go = new { data = new { ui_status = "" } };
                            var ans1 = JsonConvert.DeserializeAnonymousType(answer, TemplItemout_new_go);
                            switch (ans1.data.ui_status)
                            {
                                case "3":
                                    WriteMessage("Передать вещи", MessageType.GiveWeapon);

                                    AcceptMobileOrdersAction.Invoke();

                                    break;
                            }

                            break;
                        //case "itemout_new_go":
                        //case "additem_go":
                        //case "invcache_go":
                        //case "invcache_gt":
                        //case "invcache_cs":
                        //case "invcache_tf":
                        //case "onlinecheck":
                        //case "setonline":
                        //case "money":
                        //case "webnotify_betsonly":
                        //    break;
                        case "newitems_go":
                            answer = ee.Data.Replace("\\\"", "\"");
                            answer = answer.Replace(":\"{", ":{");
                            answer = answer.Replace("}\"}", "}}");
                            answer = answer.Replace("\\\"\\\\u", "").Replace("\\\"\",", "\\\",");
                            WSItm t = JsonConvert.DeserializeObject<WSItm>(answer);

                            id = t.data.i_classid + "_" + t.data.i_instanceid;
                            var ItemInList = Items.Find(item => item.id == id); //&& item.price >= (t.data.ui_price));

                            if (ItemInList != null && (ItemInList.price >= (t.data.ui_price)))
                            {
                                if (CLIENT1.Buy(ItemInList, cfg.key) || CLIENT1.Buy(ItemInList, cfg.key, 100))
                                {
                                    WriteMessage(string.Format("*сокеты* {0} куплен за {1} ({2})", ItemInList.name, t.data.ui_price, ItemInList.price), MessageType.BuyWeapon);
                                }
                                else
                                {
                                    WriteMessage(string.Format("*сокеты* Не успел купить {0} выставлен за {1} ({2})", ItemInList.name, t.data.ui_price, ItemInList.price), MessageType.Socket);
                                }


                            }
                            break;
                            //case "history_go":
                            //    /*answer = ee.Data.Replace(@"\", ""); ;
                            //    System.Text.RegularExpressions.Regex RID = new System.Text.RegularExpressions.Regex(@"\d+-\d+");
                            //    System.Text.RegularExpressions.Regex RPrice = new System.Text.RegularExpressions.Regex(@"\d+\.\d+");
                            //    if (RID.IsMatch(answer))
                            //    {
                            //        var Iid=RID.Match(answer);
                            //        var IPrice = RPrice.Match(answer);
                            //        var itm=Items.Find(item => item.id == Iid.Value);
                            //        if (itm!=null && Convert.ToDouble(IPrice)>itm.price)
                            //        {
                            //            CorrectPrice(itm);
                            //        }
                            //    }*/
                            //    break;
                            //                        default:
                            //#if DEBUG
                            //                            System.Diagnostics.Debug.WriteLine(ee.Data);
                            //#endif
                            //                            WriteMessage(string.Format("Сообщение: \n\r{0}", ee.Data));
                            //                            break;

                    }



                }
                catch (Exception e)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine(e.Message + Environment.NewLine + ee.Data);


                    if (type.type != "newitems_go" && type.type != "webnotify")
                        Console.WriteLine("Error " + type.type + " " + e.Message + Environment.NewLine + Environment.NewLine + answer1 + Environment.NewLine + Environment.NewLine + answer + Environment.NewLine + Environment.NewLine);
#endif
                }

            };

            client.OnClose += (ss, ee) =>
            {
                WriteMessage(string.Format("Отключен от {0}", host), MessageType.Info);
                if (!Close)
                {
                    SocketsAuth();
                }
            };



        }

        /// <summary>
        /// Авторизация в сокетах и подключение к каналу оповещений
        /// </summary>
        private void SocketsAuth()
        {
            string wskey = CLIENT.GetWS(cfg.key);
            WS t = JsonConvert.DeserializeObject<WS>(wskey);

            client.Connect();
            if (t.error == null && t.wsAuth != null)
            {
                client.Send(t.wsAuth);
                client.Send("newitems_go");
            }
            else
                Console.WriteLine(t.error);

        }




        /// <summary>
        /// Поток проходящий вещи
        /// </summary>
        public void find()
        // async Task find()
        {
            functions bot = new functions();
            while (!Close)
            {
                try
                {
                    for (int i = 0; i < Items.Count; i++)
                    {
                        {
                            // if (CLIENT.Buy(Items[i], cfg.key))
                            if (bot.Buy(Items[i], cfg.key))
                            {
                                WriteMessage(string.Format("*find* {0} куплен. Цена на покупку ({1})", Items[i].name, Items[i].price), MessageType.BuyWeapon);
                            }
                            Thread.Sleep(cfg.Itimer);
                            //await Task.Delay(cfg.Itimer);
                        }
                    }
                    //await Task.Delay(0);
                }
                catch { }
            }

        }

        /// <summary>
        /// Запуск покупок. Выставляем цены, проверяем быстрые покупки, включаем обычный поиск и сокеты
        /// </summary>
        public void Start()
        {
            UpdateDiscount();

            WriteMessage("Скидка " + cfg.discount + " %", MessageType.Info);

            CorrectPrice();
            // CorrectOrders();
            CorrectNotifications();

            WriteMessage("Цены скорректированы", MessageType.Info);

            //проверка быстрых покупок
            QuickOrderAction.BeginInvoke(null, null);
            //запуск поиска
            if (Items.Count > 0)
            {
                Find = new Thread(find);
                Find.Priority = ThreadPriority.Highest;
                Find.Start();
                // Finding = find();
                WriteMessage("Поиск включен", MessageType.Info);

            }
            //запуск сокетов

            SocketsAuth();

            //запуск таймеров
            UpdatePriceTimer.Enabled = true;
            pinPongTimer.Enabled = true;
            WriteMessage("Таймеры включены", MessageType.Info);

        }

        /// <summary>
        /// Обновление скидки
        /// </summary>
        private void UpdateDiscount()
        {
            var disc = CLIENT.GetDiscounts(cfg.key);
            if (disc != 10.01 && disc != 10.02 && disc != 10.03)
            {
                cfg.discount = disc;
                Config.Save(cfg);
            }
            else
            {
                if (disc == 10.01)
                {
                    WriteMessage("Неверный ключ", MessageType.Error);
                    Console.ReadKey();
                    Environment.Exit(-1);
                    return;
                }
            }
            // WriteMessage("Скидка " + cfg.discount + " %");
        }

        /// <summary>
        /// Обновление цен
        /// </summary>
        void CorrectPrice()
        {

            try
            {
                double Averange, MinPrice, Discount, Cost;
                Discount = 1 - Convert.ToDouble(cfg.discount / 100);
                int count = 0;
                foreach (Itm I in Items)
                {
                    //Если предмет добавлен с уведомлений, то его цену прверяем в списке уведомлений
                    if (I.priceCheck == PriceCheck.Notification)
                    {
                        var ans = CLIENT1.GetNotifications(cfg.key);
                        if (ans != null && ans.success)
                        {
                            var item = ans.Notifications.Find(fi => fi.i_classid + "_" + fi.i_instanceid == I.id);
                            if (item != null)
                            {
                                I.price = Convert.ToInt32(item.n_val) / 100.0;
                            }
                        }
                    }
                    else
                    {
                        //проверяем остальные вещи

                        Thread.Sleep(cfg.Itimer);
                        // new System.Action(() => Console.Write("\r{0}/{1} ", ++count, Items.Count)).BeginInvoke(null, null);

                        MinPrice = Convert.ToDouble(CLIENT.GetMinPrice(I, cfg.key));

                        if (MinPrice == -1)
                        {
                            MinPrice = Convert.ToDouble(CLIENT.GetMinPrice(I, cfg.key));
                            if (MinPrice == -1)
                                continue;
                        }

                        Averange = Convert.ToDouble(CLIENT.GetAverangePrice(I, cfg.key));

                        if (Averange < MinPrice)
                        {
                            Cost = Averange;
                        }
                        else
                        {
                            Cost = MinPrice;
                        }

                        //если у предмета выставлена персональная прибыль, то используем ее, а не общую
                        double profit;
                        if (I.profit != 0)
                        {
                            profit = I.profit;
                        }
                        else
                        {
                            profit = cfg.price;
                        }

                        if (Cost > 20000)
                        {
                            I.price = Math.Round((Cost * Discount - profit * 2) / 100.0, 2);
                            continue;
                        }
                        if (Cost > 10000)
                        {
                            I.price = Math.Round((Cost * Discount - profit) / 100.0, 2);
                            continue;
                        }
                        else
                        {
                            I.price = Math.Round((Cost * Discount - profit) / 100.0, 2);
                            continue;
                        }

                    }
                }

                WriteMessage("Обновлены цены", MessageType.Info);
            }
            catch (Exception ex)
            {
                WriteMessage(string.Format("[CorrectPrice] {0}", ex.Message), MessageType.Error);
            }


        }

        /// <summary>
        /// быстрые покупки
        /// </summary>
        void QuickOrderF()
        {
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
                    if (Convert.ToDouble(findItem.l_paid) < tempItem.price * 100)
                    {
                        //покупаем
                        if (CLIENT.QBuy(cfg.key, findItem.ui_id))
                        {
                            WriteMessage(string.Format("*{3}* {0} куплен за {1} ({2})", tempItem.name, Convert.ToDouble(findItem.l_paid) / 100, tempItem.price, "Список"), MessageType.BuyWeapon);
                            if (autoConfirmTrades)
                                AcceptTradeAction.Invoke();
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

                WriteMessage("Список сохранен", MessageType.Info);
            }
        }

        /// <summary>
        /// Обновление цен и проверка обменов
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void PriceCorrectTimer(object sender, ElapsedEventArgs e)
        {
            WriteMessage("Таймер", MessageType.Timer);

            //выставитьToolStripMenuItem_Click(sender, e);
            UpdatePriceDelegateAction.BeginInvoke(CallbackUpdater, null);

            if (autoConfirmTrades)
            {
                AcceptTradeAction.Invoke();
                AcceptMobileOrdersAction.Invoke();
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
            QuickOrderAction.Invoke();

            if (autoConfirmTrades)
            {
                var info = CLIENT1.CountItemsToTransfer(cfg.key);
                if (info?.getCount > 0)
                    AcceptTradeAction.Invoke();
                if (info?.outCount > 0)
                    AcceptMobileOrdersAction.Invoke();
            }

        }

        /// <summary>
        /// Функция подтверждение трейдов
        /// </summary>
        void AcceptTrades()
        {
            // WriteMessage("---AcceptTrades");
            //lock (tradesIn)

            //WriteMessage("---Заблокировано принятием");
            try
            {

                if (tradesIn)
                {
                    tradesIn = false;

                    start:
                    ///проверяем, есть ли трейды, которые нам уже отправили. Если есть, то подтверждаем их
                    var tr = CLIENT1.MarketTrades(cfg.key);
                    if (tr != null && tr.trades[0].dir == "out")
                    {
                        WriteMessage("Есть трейд", MessageType.Info);
                        var ans = offerHandler.AcceptTradeOffer(Convert.ToUInt32(tr.trades[0].trade_id), Convert.ToUInt32(tr.trades[0].bot_id), _account.AuthContainer, "1");
                        if (ans?.TradeId != null)
                        {
                            WriteMessage("Обмен принят!", MessageType.Info);
                            Thread.Sleep(5000);
                            //Проверить есть ли еще вещи на прием
                            var trades = CLIENT1.GetTrades(cfg.key);

                            if (trades?.items.Count > 0)
                                foreach (var it in trades.items)
                                    if (it.ui_status == "4")
                                        goto start;
                        }
                    }
                    else
                    {

                        int b = 0, of = 0;

                        bot:
                        var bid = CLIENT.GetBotID(cfg.key);
                        if (bid != null && bid != "0")
                        {
                            WriteMessage("Определяем бота", MessageType.Info);
                            WriteMessage("Получаем трейд", MessageType.Info);
                            offer:
                            Thread.Sleep(2000);
                            var offer = CLIENT.GetOffer(bid, cfg.key);
                            if (offer != null)
                            {

                                WriteMessage("Подтверждаем", MessageType.Info);
                                var ans = offerHandler.AcceptTradeOffer(Convert.ToUInt32(offer.trade), Convert.ToUInt32(offer.botid), _account.AuthContainer, "1");
                                if (ans?.TradeId != null)
                                {
                                    WriteMessage("Обмен принят!", MessageType.Info);
                                    Thread.Sleep(10000);
                                    //Проверить есть ли еще вещи на прием

                                    var trades = CLIENT1.GetTrades(cfg.key);

                                    if (trades?.items.Count > 0)
                                        foreach (var it in trades.items)
                                            if (it.ui_status == "4")
                                                goto start;
                                }
                                else
                                    WriteMessage("Обмен не принят", MessageType.Info);

                            }
                            else
                            {
                                WriteMessage("Не дает оффер", MessageType.Info);
                                tr = CLIENT1.MarketTrades(cfg.key);
                                if (of++ < 2 && tr != null && tr.success && tr.trades.Count > 0)
                                    goto offer;
                            }
                        }
                        else
                        {
                            if (b++ < 2) goto bot;
                        }
                    }
                    //обновляем инвентарь
                    CLIENT1.UpdateInvent(cfg.key);
                }
            }

            catch (Exception exx)
            {
                //Console.WriteLine(exx.GetBaseException() + "\n\n" + exx.InnerException + "\n\n" + exx.Source + "\n\n" + exx.StackTrace + "\n\n" + exx.TargetSite);
                StreamWriter sw = new StreamWriter("log.log", true);
                sw.WriteLineAsync("------------"
                    + string.Format("[{0}] ", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00:") + DateTime.Now.Second.ToString("00")) + "\n1)"
                    + exx.Data.Keys + "\n2)"
                    + exx.Data.Values + "\n3)"
                    + exx.GetBaseException() + "\n4)"
                    + exx.InnerException + "\n5)"
                    + exx.Source + "\n6)"
                    + exx.StackTrace + "\n7)"
                    + exx.TargetSite + "\n8)"
                    + exx.Data
                    + "\n------------\n");
                sw.Close();
            }
            finally
            {
                tradesIn = true;
            }

            // }
            //WriteMessage("---Разблокировано принятием");
        }

        /// <summary>
        /// подтвердить трейд
        /// </summary>
        public void ConfirmTrade()
        {
            if (IAcceptInTradesResult == null)
                IAcceptInTradesResult = AcceptTradeAction.BeginInvoke(null, null);
            else
                if (IAcceptInTradesResult.IsCompleted)
                IAcceptInTradesResult = AcceptTradeAction.BeginInvoke(null, null);

        }

        /// <summary>
        /// Добавление предмета в список покупок
        /// </summary>
        /// <param name="link">ссылка на страницу с предметом</param>
        public void AddItem(string link, int _price = 0, PriceCheck _check = PriceCheck.Price)
        {

            var itm = new Itm { link = link, price = _price, priceCheck = _check };

            itm.hash = CLIENT.GetHash(itm, cfg.key);
            //проверяем, нет ли этого предмета в списке
            var ch = Items.Find(p => p.id == itm.id);
            if (ch != null)
            {
                WriteMessage("Данный предмет уже есть", MessageType.Info);
                return;
            }

            if (itm.name != "")
            {
                Items.Add(itm);
                WriteMessage($"Добавлен {itm.name}\nЦена {itm.price} рублей", MessageType.Info);
                Save(false);
            }
            else
            {
                WriteMessage($"Ошибка при добавлении. Попробуйте еще раз.", MessageType.Error);
            }
        }

        /// <summary>
        /// Обновление цен на предметы
        /// </summary>
        public void UpdatePrice()
        {
            UpdateDiscount();
            UpdatePriceDelegateAction.BeginInvoke(null, null);
        }
        /// <summary>
        /// Проверка быстрых покупок
        /// </summary>
        public void CheckQuickOrders()
        {
            QuickOrderAction.BeginInvoke(null, null);
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
            catch (Exception ex)
            {
                WriteMessage(string.Format("Ошибка в выставлении прибыли/r/nМетод {0}/r/nОшибка:{1}", System.Reflection.MethodInfo.GetCurrentMethod().Name, ex.Message), MessageType.Error);
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
            //снятие ордера
            Items[index].price = 0;
            CLIENT.ProcessOrder(Items[index], cfg.key);
            //удаление уведомления
            CLIENT.Notification(Items[index], cfg.key);
            ///удаление предмета из списка
            Items.RemoveAt(index);
            //сохранение изменений
            Save(false);
        }


        public void SetUpdateTimer(string time)
        {
            cfg.UpdatePriceTimerTime = Convert.ToInt16(time);
            Config.Save(cfg);
            UpdatePriceTimer.Interval = cfg.UpdatePriceTimerTime * 60 * 1000;
        }

        public double GetUpdateTimer()
        {
            return cfg.UpdatePriceTimerTime;
        }
        public void AcceptMobileOrdersFunc()
        {
            if (IAcceptOutTradesResult == null)
                IAcceptOutTradesResult = AcceptMobileOrdersAction.BeginInvoke(null, null);
            else
                if (IAcceptOutTradesResult.IsCompleted)
                IAcceptOutTradesResult = AcceptMobileOrdersAction.BeginInvoke(null, null);
        }
        /// <summary>
        /// Включено ли автоподтверждение трейдов или нет
        /// </summary>
        public bool GetConfirmTradesValue()
        {
            return autoConfirmTrades;
        }

        public void TurnOnOrOffAutoConfirmTrades()
        {
            if (autoConfirmTrades)
            {
                autoConfirmTrades = false;
                return;
            }

            Trades();
            AcceptTradeAction.Invoke();

        }

        /// <summary>
        /// Запустить обновление рдеров
        /// </summary>
        public void UpdateOrdersAndNotifications()
        {
            //UpdateOrdersDelegate.Invoke();
            UpdateNotificationsDelegate.BeginInvoke(null, null);
        }

        /// <summary>
        /// Выставление оредеров автопокупок
        /// </summary>
        private void CorrectOrders()
        {
            try
            {
                string ans;
                foreach (Itm I in Items)
                {
                    ans = CLIENT.ProcessOrder(I, cfg.key);
                    if (!ans.Contains("error"))
                        Thread.Sleep(cfg.Itimer);
                    else
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine(ans);
#endif
                    }
                }
                WriteMessage("Обновлены ордеры", MessageType.Info);
            }
            catch (Exception ex)
            {
                WriteMessage(string.Format("[Correctorders] {0}", ex.Message), MessageType.Error);
            }
        }

        void CorrectOrdersAndNotifications(IAsyncResult result)
        {
            if (result.IsCompleted)
            {
                // UpdateOrdersDelegate.BeginInvoke(null, null);
                UpdateNotificationsDelegate.Invoke();
                CheckNotificationsDelegate.Invoke();
            }
        }
        /// <summary>
        /// Удалить все ордеры и оповещение
        /// </summary>
        public void Closing()
        {
            Close = true;


            UpdatePriceTimer.Stop();
            pinPongTimer.Stop();

            UpdatePriceTimer.Dispose();
            pinPongTimer.Dispose();


            client.Close();

            // Finding.Wait();
            // Finding.Dispose();

            CLIENT.DeleteAllOrders(cfg.key);

            foreach (Itm I in Items)
            {
                CLIENT.Notification(I, cfg.key, 1);
                Thread.Sleep(cfg.Itimer);
            }

            CLIENT.GoOffline(cfg.key);
        }

        /// <summary>
        /// Обновление уведомлений
        /// </summary>
        void CorrectNotifications()
        {
            try
            {
                string ans;
                foreach (Itm I in Items)
                {
                    ans = CLIENT.Notification(I, cfg.key) ?? "error";
                    if (!ans.Contains("error"))
                        Thread.Sleep(cfg.Itimer);
                    else
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine(ans);
#endif
                    }
                }
                WriteMessage("Обновлены уведомления", MessageType.Info);
            }
            catch (Exception ex)
            {

                WriteMessage(string.Format("[CorrectNotifications] {0}", ex.Message), MessageType.Error);

            }
        }

        /// <summary>
        /// Вывод сообщения в консоль
        /// </summary>
        /// <param name="msg">Текст</param>
        /// <param name="msgType">Тип сообщения</param>
        void WriteMessage(string msg, MessageType msgType)
        {
            switch (msgType)
            {
                case MessageType.BuyWeapon:
                    if (cfg.Messages[MessageType.BuyWeapon])
                    {
                        new System.Action(() =>
                        {
                            Console.ResetColor();
                            Console.Write(string.Format("[{0}] ", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00:") + DateTime.Now.Second.ToString("00")));
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine(msg);
                            Console.ResetColor();
                        }).Invoke();
                    }
                    break;

                case MessageType.Error:
                    if (cfg.Messages[MessageType.Error])
                    {
                        new System.Action(() =>
                        {
                            Console.ResetColor();
                            Console.Write(string.Format("[{0}] ", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00:") + DateTime.Now.Second.ToString("00")));
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine(msg);
                            Console.ResetColor();
                        }).Invoke();
                    }
                    break;

                case MessageType.GetWeapon:
                    if (cfg.Messages[MessageType.GetWeapon])
                    {
                        new System.Action(() =>
                        {
                            Console.ResetColor();
                            Console.Write(string.Format("[{0}] ", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00:") + DateTime.Now.Second.ToString("00")));
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine(msg);
                            Console.ResetColor();
                        }).Invoke();
                    }
                    break;

                case MessageType.GiveWeapon:
                    if (cfg.Messages[MessageType.GiveWeapon])
                    {
                        new System.Action(() =>
                        {
                            Console.ResetColor();
                            Console.Write(string.Format("[{0}] ", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00:") + DateTime.Now.Second.ToString("00")));
                            Console.ForegroundColor = ConsoleColor.Magenta;
                            Console.WriteLine(msg);
                            Console.ResetColor();
                        }).Invoke();
                    }
                    break;

                case MessageType.Info:
                    if (cfg.Messages[MessageType.Info])
                    {
                        new System.Action(() =>
                        {
                            Console.ResetColor();
                            Console.Write(string.Format("[{0}] ", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00:") + DateTime.Now.Second.ToString("00")));
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.WriteLine(msg);
                            Console.ResetColor();
                        }).Invoke();
                    }
                    break;

                case MessageType.Socket:
                    if (cfg.Messages[MessageType.Socket])
                    {
                        new System.Action(() =>
                        {
                            Console.ResetColor();
                            Console.Write(string.Format("[{0}] ", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00:") + DateTime.Now.Second.ToString("00")));
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.WriteLine(msg);
                            Console.ResetColor();
                        }).Invoke();
                    }
                    break;

                case MessageType.Timer:
                    if (cfg.Messages[MessageType.Timer])
                    {
                        new System.Action(() =>
                        {
                            Console.ResetColor();
                            Console.Write(string.Format("[{0}] ", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00:") + DateTime.Now.Second.ToString("00")));
                            Console.ForegroundColor = ConsoleColor.Blue;
                            Console.WriteLine(msg);
                            Console.ResetColor();
                        }).Invoke();
                    }
                    break;
            }


        }

        public string Status()
        {
            if (Find != null)
                return string.Format("Find {0}", Find.ThreadState);
            else
                return string.Format("Find {0}", Finding.Status);
        }



        public string GetProfit()
        {
            var p = CLIENT.GetProfit(cfg.key);
            if (p != -1 && p != -2)
            {
                return ("Получено за день " + p / 100.0 + " рублей");
            }
            else
            {
                return ("Ошибка");
            }
        }

        private void SteamAuthLogin()
        {
            _config = ConfigHandler.Reload(_config);
            var authLogin = new UserLogin(_config.Username, _config.Password);
            LoginResult result;
            do
            {
                result = authLogin.DoLogin();
                switch (result)
                {
                    case LoginResult.NeedEmail:
                        Console.Write("An email was sent to this account's address, please enter the code here to continue: ");
                        authLogin.EmailCode = Console.ReadLine();
                        break;
                    case LoginResult.NeedCaptcha:
                        Console.WriteLine("https://steamcommunity.com/public/captcha.php?gid=" + authLogin.CaptchaGID);
                        Console.Write("Please enter the captcha that just opened up on your default browser: ");
                        authLogin.CaptchaText = Console.ReadLine();
                        break;
                    case LoginResult.Need2FA:
                        Console.Write("Please enter in your authenticator code: ");
                        authLogin.TwoFactorCode = Console.ReadLine();
                        break;
                    default:
                        throw new Exception("Case was not accounted for. Case: " + result);
                }
            } while (result != LoginResult.LoginOkay);

            AuthenticatorLinker linker = new AuthenticatorLinker(authLogin.Session);
            Console.Write("Please enter the number you wish to associate this account in the format +1XXXXXXXXXX where +1 is your country code, leave blank if no new number is desired: ");

            string phoneNumber = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(phoneNumber)) phoneNumber = null;
            linker.PhoneNumber = phoneNumber;

            AuthenticatorLinker.LinkResult linkResult = linker.AddAuthenticator();
            if (linkResult != AuthenticatorLinker.LinkResult.AwaitingFinalization)
            {
                Console.WriteLine("Could not add authenticator: " + linkResult);
                Console.WriteLine(
                    "If you attempted to link an already linked account, please tell FatherFoxxy to get off his ass and implement the new stuff.");
                return;
            }

            if (!SaveMobileAuth(linker))
            {
                Console.WriteLine("Issue saving auth file, link operation abandoned.");
                return;
            }

            Console.WriteLine(
                "You should have received an SMS code, please input it here. If the code does not arrive, please input a blank line to abandon the operation.");
            AuthenticatorLinker.FinalizeResult finalizeResult;
            do
            {
                Console.Write("SMS Code: ");
                string smsCode = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(smsCode)) return;
                finalizeResult = linker.FinalizeAddAuthenticator(smsCode);

            } while (finalizeResult != AuthenticatorLinker.FinalizeResult.BadSMSCode);
        }

        private bool SaveMobileAuth(AuthenticatorLinker linker)
        {
            try
            {
                string sgFile = JsonConvert.SerializeObject(linker.LinkedAccount, Formatting.Indented);
                const string fileName = "account.maFile";
                File.WriteAllText(fileName, sgFile);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        internal void CreateAuth()
        {
            while (!File.Exists("account.maFile"))
                SteamAuthLogin();
            var sgAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText("account.maFile"));
        }

        internal void GenerateGuardCode()
        {
            if (accountFileExist)
            {
                var sgAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText("account.maFile"));
                Console.WriteLine(sgAccount.GenerateSteamGuardCode());
            }
            else
            {
                Console.WriteLine("File not exist!");
            }
        }

        private void AcceptConfirmations(SteamGuardAccount sgAccount)
        {
            sgAccount.Session.SteamLogin = _account.FindCookieByName("steamlogin").Value;
            sgAccount.Session.SteamLoginSecure = _account.FindCookieByName("steamloginsecure").Value;

            sgAccount.AcceptConfirmation(new Confirmation() { });
            foreach (Confirmation confirmation in sgAccount.FetchConfirmations())
            {
                WriteMessage("Подтверждаем", MessageType.Info);
                if (sgAccount.AcceptConfirmation(confirmation))
                {
                    WriteMessage("Подтвержден", MessageType.Info);
                }
            }
        }

        private void AcceptMobile()
        {
            // WriteMessage("---AcceptMobile");
            try
            {
                if (accountFileExist)
                {
                    //  lock (tradesOut)
                    if (tradesOut)
                    {
                        tradesOut = false;
                        // WriteMessage("---Заблокировано выводом");
                        if (autoConfirmTrades == true)
                        {
                            var sgAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText("account.maFile"));
                            AcceptConfirmations(sgAccount);
                        }


                        var offer = CLIENT.GetOffer("1", cfg.key, "in");
                        if (offer != null)
                        {
                            Trade:
                            WriteMessage("Запрос ордера", MessageType.Info);
                            WriteMessage("Ордер получен", MessageType.Info);
                            try
                            {
                                var ans = offerHandler.AcceptTradeOffer(Convert.ToUInt32(offer.trade), Convert.ToUInt32(offer.botid), _account.AuthContainer, "1");

                            }
                            catch (Exception exx)
                            {
                                StreamWriter sw = new StreamWriter("log.log", true);
                                sw.WriteLineAsync("------------"
                                    + string.Format("[{0}] ", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00:") + DateTime.Now.Second.ToString("00")) + "\n1)"
                                    + exx.Data.Keys + "\n2)"
                                    + exx.Data.Values + "\n3)"
                                    + exx.GetBaseException() + "\n4)"
                                    + exx.InnerException + "\n5)"
                                    + exx.Source + "\n6)"
                                    + exx.StackTrace + "\n7)"
                                    + exx.TargetSite + "\n8)"
                                    + exx.Data
                                    + "\n------------\n");
                                sw.Close();
                            }
                            finally
                            {
                                if (autoConfirmTrades == true)
                                {
                                    var sgAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText("account.maFile"));
                                    AcceptConfirmations(sgAccount);
                                }
                            }

                            Thread.Sleep(10000);
                            offer = CLIENT.GetOffer("1", cfg.key, "in");
                            if (offer != null)
                            {
                                WriteMessage("Есть предметы на передачу!", MessageType.Info);
                                goto Trade;
                            }
                        }

                    }
                    // WriteMessage("---Разблокировано выводом");
                }
            }
            finally
            {
                tradesOut = true;
            }
        }

        public void ReloadBool()
        {
            tradesIn = true;
            tradesOut = true;
        }
        //

        /// <summary>
        /// Проверяем, есть ли предметы в уведомлениях, которые нужно добавить в покупки
        /// </summary>
        public void CheckNotifications()
        {
            try
            {
                int i = 0;
                var ans = CLIENT1.GetNotifications(cfg.key);
                if (ans != null && ans.success)
                {
                    foreach (var itm in ans.Notifications)
                    {
                        var haveInItems = Items.Find(item => item.id == itm.i_classid + "_" + itm.i_instanceid);
                        if (haveInItems == null)
                        {
                            AddItem("https://market.csgo.com/item/" + itm.i_classid + "-" + itm.i_instanceid, Convert.ToInt32(itm.n_val) / 100, PriceCheck.Notification);
                            i++;
                        }
                    }
                    if(i!=0)
                        WriteMessage($"Добавлено {i} предметов", MessageType.Info);
                }
               
            }
            catch
            {

            }
        }

        /// <summary>
        /// Выставление персональной прибыли для предмета
        /// </summary>
        /// <param name="_id">Номер предмета в списке</param>
        /// <param name="_profit">Прибыль</param>
        /// <returns></returns>
        public bool SetProfit(int _id, int _profit)
        {
            try
            {
                Items[_id].profit = _profit;
                Save(false);
                return true;
            }
            catch
            {
                return false;
            }

        }
        /// <summary>
        /// Тип проверки цены на предмет
        /// </summary>
        /// <param name="_id">Номер предмета</param>
        /// <returns></returns>
        public PriceCheck GetPriceCheck(int _id)
        {
            return Items[_id].priceCheck;
        }

        /// <summary>
        /// Установить тип проверки цены
        /// </summary>
        /// <param name="_id">Номер предмета</param>
        /// <param name="_type">Тип</param>
        /// <returns></returns>
        public bool SetPriceCheck(int _id, int _type)
        {
            try
            {
                var type = (PriceCheck)_type;
                Items[_id].priceCheck = type;
                Save(false);
                return true;
            }
            catch
            {
                return false;
            }
        }



    }
}
