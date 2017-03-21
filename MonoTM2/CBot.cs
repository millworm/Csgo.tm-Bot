﻿
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
        bool Close = false;
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
        bool confirmOffer = false;
        //конфиг
        Config cfg = new Config();
        //
        //delegate void voidDelegate();

        Thread Find;
        Task Finding;

        functions CLIENT, CLIENT1;
        private WebSocket client;
        const string host = "wss://wsn.dota2.net/wsn/";
        public List<Itm> Items = new List<Itm>();


        System.Action UpdatePriceDelegate = null;
        System.Action QuickOrder = null;
        System.Action AcceptTrade = null;
        System.Action UpdateOrdersDelegate = null;
        System.Action UpdateNotificationsDelegate = null;

        AsyncCallback CallbackUpdater = null;

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

                if (File.Exists("account.maFile"))
                {
                    GenerateGuardCode();
                }
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
            }
            catch(Exception ex) {
                autoConfirmTrades = false;
                Console.WriteLine(ex.Message);
            }


        }

        public void Loader()
        {


            AcceptTrade = AcceptTrades;
            Console.WriteLine("Автоматически получать вещи?");
            var w = Console.ReadLine();
            if (w.ToLower() == "y")
            {
                Trades();
            }


            CallbackUpdater = new AsyncCallback(CorrectOrdersAndNotifications);

            UpdatePriceDelegate = CorrectPrice;
            UpdateOrdersDelegate = CorrectOrders;
            UpdateNotificationsDelegate = CorrectNotifications;
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
                cfg.key = "";
                cfg.discount = 10;
                Config.Reload(cfg);
            }
            else
            {
                cfg = Config.Reload(cfg);

            }




            //таймер обновления цен
            UpdatePriceTimer.Elapsed += new ElapsedEventHandler(PriceCorrectTimer);
            UpdatePriceTimer.Interval = cfg.UpdatePriceTimerTime * 60 * 1000;

            pinPongTimer.Elapsed += new ElapsedEventHandler(PingPongTimer);
            pinPongTimer.Interval = cfg.PingPongTimerTime * 60 * 1000;

            client = new WebSocket(host);

            string id = "", answer = "", answer1 = "";

            client.OnOpen += (ss, ee) =>
                Console.WriteLine(string.Format("Подключение к {0} успешно ", host));

            client.OnError += (ss, ee) =>
            {
                WriteMessage(string.Format("Ошибка: {0}\n{1}" + Environment.NewLine, ee.Message, ee.Exception), ConsoleColor.Red);
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

                            if (ItemInListWeb != null)//&& (ItemInListWeb.price >= (ansWeb.data.price)))
                            {
                                if (CLIENT1.Buy(ItemInListWeb, cfg.key))
                                {
                                    WriteMessage(string.Format("[webnotify] {0} куплен за {1} ({2})", ItemInListWeb.name, ansWeb.data.price, ItemInListWeb.price), ConsoleColor.Green);
                                }
                                //else
                                //{
                                //    WriteMessage(string.Format("[webnotify] Не успел купить {0} выставлен за {1} ({2})", ItemInListWeb.name, ansWeb.data.price, ItemInListWeb.price));
                                //}


                            }
                            //}
                            break;
                        case "itemstatus_go":
                            //{"type":"itemstatus_go","data":"{\"id\":\"173474064\",\"status\":5}"}
                            //WriteMessage("itemstatus_go " + ee.Data);
                            answer = ee.Data.Replace("\\\"", "\"");
                            answer = answer.Replace(":\"{", ":{");
                            answer = answer.Replace("}\"}", "}}");
                            var itemstatus_go = new { data = new { status="" } };
                            var ansItem = JsonConvert.DeserializeObject<TrOf>(answer);
                            switch (ansItem.data.status)
                            {
                                case 4:
                                    if (autoConfirmTrades)
                                    {
                                        AcceptTrade.BeginInvoke(null, null);
                                    }
                                    WriteMessage(string.Format("[{0}] Забрать вещи", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00")), ConsoleColor.Cyan);
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
                            var TemplItemout_new_go = new { data = new { ui_status="" } };
                            var ans1 = JsonConvert.DeserializeAnonymousType(answer, TemplItemout_new_go);
                            switch (ans1.data.ui_status)
                            {
                                case "3":
                                    WriteMessage(string.Format("[{0}] Передать вещи", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00")), ConsoleColor.Magenta);
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
                                if (CLIENT1.Buy(ItemInList, cfg.key))
                                {
                                    WriteMessage(string.Format("[сокеты] {0} куплен за {1} ({2})", ItemInList.name, t.data.ui_price, ItemInList.price), ConsoleColor.Green);
                                }
                                //else
                                //{
                                //    WriteMessage(string.Format("[сокеты] Не успел купить {0} выставлен за {1} ({2})", ItemInList.name, t.data.ui_price, ItemInList.price));
                                //}


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

#endif
                    if (type.type != "newitems_go" && type.type != "webnotify")
                        Console.WriteLine("Error " + type.type + " " + e.Message + Environment.NewLine + Environment.NewLine + answer1 + Environment.NewLine + Environment.NewLine + answer + Environment.NewLine + Environment.NewLine);
                }

            };

            client.OnClose += (ss, ee) =>
            {
                WriteMessage(string.Format("Отключен от {0}", host));
                if (!Close)
                {
                    string wskey = CLIENT.GetWS(cfg.key);
                    WS t = JsonConvert.DeserializeObject<WS>(wskey);

                    client.Connect();
                    if (t.error == null && t.wsAuth != null)
                        client.Send(t.wsAuth);
                    else
                        Console.WriteLine(t.error);
                     client.Send("newitems_go");
                }
            };


          
        }



        
        /// <summary>
        /// Поток проходящий вещи
        /// </summary>
        // public void find()
        async Task find()
        {
            while (!Close)
            {
                try
                {
                    for (int i = 0; i < Items.Count; i++)
                    {
                        {
                            if (CLIENT.Buy(Items[i], cfg.key))
                            {
                                WriteMessage(string.Format("[{2}] find {0} куплен. Цена на покупку ({1})", Items[i].name, Items[i].price,
                                    DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00")), ConsoleColor.Green);
                            }
                            // Thread.Sleep(cfg.Itimer);
                            await Task.Delay(cfg.Itimer);
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
            CorrectPrice();
            // CorrectOrders();
            CorrectNotifications();

            WriteMessage("Цены скорректированы", ConsoleColor.Yellow);

            //проверка быстрых покупок
            QuickOrder.BeginInvoke(null, null);
            //запуск поиска
            if (Items.Count > 0)
            {
                //  Find = new Thread(find);
                // Find.Start();
                Finding = find();
                WriteMessage("Поиск включен", ConsoleColor.Yellow);

            }
            //запуск сокетов
            string wskey = CLIENT.GetWS(cfg.key);
            WS t = JsonConvert.DeserializeObject<WS>(wskey);
            client.Connect();
            if (t.error == null && t.wsAuth != null)
                client.Send(t.wsAuth);
            else
                WriteMessage(t.error, ConsoleColor.Red);
            Thread.Sleep(0);


             client.Send("newitems_go");

            //запуск таймеров
            UpdatePriceTimer.Enabled = true;
            pinPongTimer.Enabled = true;
            WriteMessage("Таймеры включены", ConsoleColor.Yellow);

        }

        /// <summary>
        /// Обновление скидки
        /// </summary>
        private void UpdateDiscount()
        {
            var disc=CLIENT.GetDiscounts(cfg.key);
            if (disc != 10.01 && disc != 10.02 && disc != 10.03)
            {
                cfg.discount = disc;
                Config.Save(cfg);
            }
            else
            {
                if(disc==10.01)
                {
                    WriteMessage("Неверный ключ", ConsoleColor.Red);
                    Thread.Sleep(5000);
                    Environment.Exit(-1);
                    return;
                }
            }
            WriteMessage("Скидка " + cfg.discount + " %");
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
                                I.turn = false;
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
                                I.turn = false;
                            }
                            continue;
                        }
                    }



                }
                //удаляем предметы, которые нет смысла покупать
               /* var del = Items.RemoveAll(item => item.turn == false);
                if (del != 0)
                    WriteMessage(string.Format("Удалено {0} предметов", del));
                */
                WriteMessage(string.Format("Обновлены цены {0}:{1}", DateTime.Now.Hour.ToString("00"), DateTime.Now.Minute.ToString("00")));
            }
            catch (Exception ex)
            {
                WriteMessage(string.Format("[CorrectPrice] {0}", ex.Message), ConsoleColor.Red);
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
                            WriteMessage(string.Format("[{4}] {3} {0} куплен за {1} ({2})", tempItem.name, Convert.ToDouble(findItem.l_paid) / 100, tempItem.price, "Список", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00")), ConsoleColor.Green);
                            AcceptTrade.BeginInvoke(null, null);
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
            WriteMessage(string.Format("[{0}] таймер", DateTime.Now.Hour.ToString("00:") + DateTime.Now.Minute.ToString("00")), ConsoleColor.Blue);

            //выставитьToolStripMenuItem_Click(sender, e);
            UpdatePriceDelegate.BeginInvoke(CallbackUpdater, null);

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
            if (!confirmOffer)
            {
                confirmOffer = true;
                try
                {
                    start:
                    ///проверяем, есть ли трейды, которые нам уже отправили. Если есть, то подтверждаем их
                    var tr = CLIENT1.MarketTrades(cfg.key);
                    if (tr != null && tr.trades[0].dir == "out")
                    {
                        var ans = offerHandler.AcceptTradeOffer(Convert.ToUInt32(tr.trades[0].trade_id), Convert.ToUInt32(tr.trades[0].bot_id), _account.AuthContainer, "1");
                    }
                    else
                    {

                        int b = 0, of = 0;

                        bot:
                        var bid = CLIENT.GetBotID(cfg.key);
                        if (bid != null && bid != "0")
                        {
                            Console.WriteLine("Определяем бота");
                            Console.WriteLine("Получаем трейд");
                            offer:
                            Thread.Sleep(2000);
                            var offer = CLIENT.GetOffer(bid, cfg.key);
                            if (offer != null)
                            {

                                Console.WriteLine("Подтверждаем");
                                var botsteamid = offer.profile.Substring(offer.profile.IndexOf("s/") + 2).Replace("/", "");
                                var ans = offerHandler.AcceptTradeOffer(Convert.ToUInt32(offer.trade), Convert.ToUInt32(offer.botid), _account.AuthContainer, "1");
                                if (ans?.TradeId != null)
                                {
                                    Console.WriteLine("Обмен принят!");
                                    //Проверить есть ли еще вещи на прием
                                    tr = CLIENT1.MarketTrades(cfg.key);
                                    if (tr != null && tr.success && tr.trades.Count > 0)
                                        goto start;
                                }
                                else
                                    Console.WriteLine("Обмен не принят");

                            }
                            else
                            {
                                Console.WriteLine("Не дает оффер");
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

                catch (Exception exx)
                {
                    Console.WriteLine(exx.GetBaseException() + "\n\n" + exx.InnerException + "\n\n" + exx.Source + "\n\n" + exx.StackTrace + "\n\n" + exx.TargetSite);

                }
                finally
                {
                    confirmOffer = false;
                }
            }
        }

        /// <summary>
        /// подтвердить трейд
        /// </summary>
        public void ConfirmTrade()
        {
            AcceptTrade.BeginInvoke(null,null);
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
            catch (Exception ex)
            {
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

            }
            Trades();
            AcceptTrade.BeginInvoke(null, null);
        }

        /// <summary>
        /// Запустить обновление рдеров
        /// </summary>
        public void UpdateOrdersAndNotifications()
        {
            //UpdateOrdersDelegate.Invoke();
            UpdateNotificationsDelegate.Invoke();
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
                Console.WriteLine(string.Format("Обновлены ордеры {0}:{1}", DateTime.Now.Hour.ToString("00"), DateTime.Now.Minute.ToString("00")));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Correctorders] {0}", ex.Message);
                Console.ResetColor();
            }
        }

        void CorrectOrdersAndNotifications(IAsyncResult result)
        {
            if (result.IsCompleted)
            {
                // UpdateOrdersDelegate.BeginInvoke(null, null);
                UpdateNotificationsDelegate.BeginInvoke(null, null);
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
                    ans = CLIENT.Notification(I, cfg.key)?? "error";
                    if (!ans.Contains("error"))
                        Thread.Sleep(cfg.Itimer);
                    else
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine(ans);
#endif
                    }
                }
                Console.WriteLine(string.Format("Обновлены уведомления {0}:{1}", DateTime.Now.Hour.ToString("00"), DateTime.Now.Minute.ToString("00")));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[CorrectNotifications] {0}", ex.Message);
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Вывод сообщения в консоль
        /// </summary>
        /// <param name="msg">текст</param>
        /// <param name="color">ConsoleColor</param>
        void WriteMessage(string msg, ConsoleColor color = ConsoleColor.Gray)
        {
            Console.ForegroundColor = color;
            new System.Action(() => Console.WriteLine(msg)).Invoke();
            Console.ResetColor();
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
            var p=CLIENT.GetProfit(cfg.key);
            if (p != -1 && p != -2)
            {
               return("Получено за день " + p / 100.0 + " рублей");
            }
            else
            {
                return ("Ошибка");
            }
        }

        private void SteamAuthLogin()
        {
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
            if (File.Exists("account.maFile"))
            {
                var sgAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText("account.maFile"));
                Console.WriteLine(sgAccount.GenerateSteamGuardCode());
            }else
            {
                Console.WriteLine("File not exist!");
            }
        }
    }
}
