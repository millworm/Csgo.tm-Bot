
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
using System.Linq;
using System.Diagnostics;

namespace MonoTM2
{
    public class CBot:IDisposable
    {
        bool autoConfirmTrades, Close, accountFileExist;

        //конфиг
        Config cfg;
        TradeWorker tradeWorker;

        Thread Find;
        Task Finding;

        functions CLIENT, CLIENT1;
        private WebSocket client;

        const string host = "wss://wsn.dota2.net/wsn/";
        List<Itm> Items = new List<Itm>();

        System.Action UpdatePriceDelegateAction, QuickOrderAction,
             UpdateOrdersDelegate, UpdateNotificationsDelegate, CheckNotificationsDelegate;

        AsyncCallback CallbackUpdater;

        System.Timers.Timer UpdatePriceTimer = new System.Timers.Timer();
        System.Timers.Timer pinPongTimer = new System.Timers.Timer();

        public void Loader()
        {
            // AcceptTradeAction = AcceptTrades;
            //загрузка настроек
            if (!File.Exists("config.json"))
            {
                cfg = new Config();

                var mm = Enum.GetValues(typeof(MessageType));
                foreach (var it in mm)
                {
                    cfg.Messages.Add((MessageType)it, true);
                }
            }
            else
                cfg = Config.Reload();

            if (!String.IsNullOrEmpty(cfg.SteamLogin))
            {
                //если есть мобильный аутентификатор включаем автоматический прием вещей
                //если его нет, то узнаем, нужно ли забирать вещи самостоятельно 
                if (File.Exists("account.maFile"))
                {
                    accountFileExist = true;
                }
                autoConfirmTrades = true;
                //else
                //{
                //    Console.WriteLine(("Автоматически получать вещи?"));
                //    var w = Console.ReadLine();
                //    if (w.ToLower() == "y")
                //    {
                //        tradeWorker.Auth();
                //        //Trades();
                //    }
                //}
            }
            else
            {
                Config.Save(cfg);

                Console.WriteLine("Заполните config.json для автоматического получения вещей");
				
            }

            tradeWorker = new TradeWorker();

            CallbackUpdater = new AsyncCallback(CorrectOrdersAndNotifications);
            if (cfg.MassUpdate)
                UpdatePriceDelegateAction = MassUpdate;
            else
                UpdatePriceDelegateAction = CorrectPrice;

            UpdateOrdersDelegate = CorrectOrders;
            UpdateNotificationsDelegate = CorrectNotifications;
            QuickOrderAction = QuickOrderF;
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

            string id = "", answer = "";

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
                                        tradeWorker.AcceptTrade(TypeTrade.OUT);
                                    }
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

                                    tradeWorker.AcceptTrade(TypeTrade.IN);
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
                        Console.WriteLine("Error " + type.type + " " + e.Message + Environment.NewLine + Environment.NewLine  + Environment.NewLine + Environment.NewLine + answer + Environment.NewLine + Environment.NewLine);
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

            if (cfg.MassUpdate)
                MassUpdate();
            else
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
            //foreach (var tempItem in Items)
            //{
            //    //ищем их быстрой покупке
            //    var findItem = QuickItemsList.Find(item => item.i_classid + "_" + item.i_instanceid == tempItem.id);

            //    //если нашли, то проверяем цену
            //    if (findItem != null)
            //    {
            //        if (Convert.ToDouble(findItem.l_paid) < tempItem.price * 100)
            //        {
            //            //покупаем
            //            if (CLIENT.QBuy(cfg.key, findItem.ui_id))
            //            {
            //                WriteMessage(string.Format("*{3}* {0} куплен за {1} ({2})", tempItem.name, Convert.ToDouble(findItem.l_paid) / 100, tempItem.price, "Список"), MessageType.BuyWeapon);
            //                if (autoConfirmTrades)
            //                    tradeWorker.AcceptTrade(TypeTrade.OUT);
            //                // AcceptTradeAction.Invoke();
            //            }
            //        }
            //    }
            //}

            var l = QuickItemsList.Where(
                q => Items.Any(
                    i => i
						.id == q.i_classid + "_" + q.i_instanceid && 
						Convert.ToDouble(q.l_paid) < i.price * 100))
			.ToList();

            if (l?.Count > 0)
            {
                int i = 0;
                l.ForEach(x =>
                {
                    if (CLIENT.QBuy(cfg.key, x.ui_id))
                    {
                        i++;
                    }
                });
                if (i != 0)
                {
                    WriteMessage("*Список* Куплен предмет", MessageType.BuyWeapon);
                    if (autoConfirmTrades)
                        tradeWorker.AcceptTrade(TypeTrade.OUT);
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
                var info = CLIENT1.CountItemsToTransfer(cfg.key);
                if (info?.getCount > 0)
                    tradeWorker.AcceptTrade(TypeTrade.OUT);
                if (info?.outCount > 0)
                    tradeWorker.AcceptTrade(TypeTrade.IN);
                //AcceptTradeAction.Invoke();
                //AcceptMobileOrdersAction.Invoke();
            }
        }

        /// <summary>
        /// Говорим сайту что мы онлайн и проверям быстрые покупки
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void PingPongTimer(object sender, ElapsedEventArgs e)
        {
            CLIENT.Ping(cfg.key);
            //
            client.Send("PING");
            //проверка быстрых покупок
            QuickOrderAction.Invoke();

            if (autoConfirmTrades)
            {
                var info = CLIENT1.CountItemsToTransfer(cfg.key);
                if (info?.getCount > 0)
                {
                    tradeWorker.AcceptTrade(TypeTrade.OUT);
                }
                if (info?.outCount > 0)
                {
                    tradeWorker.AcceptTrade(TypeTrade.IN);
                }
            }
        }


        /// <summary>
        /// подтвердить трейд
        /// </summary>
        public void ConfirmTrade()
        {
            tradeWorker.AcceptTrade(TypeTrade.OUT);
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
            tradeWorker.AcceptTrade(TypeTrade.IN);
        }
        public void AcceptMobileOrders()
        {
            tradeWorker.AcceptTrade(TypeTrade.MOBILE);
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
            else
            {
                autoConfirmTrades = true;
            }
            tradeWorker.Auth();
            tradeWorker.AcceptTrade(TypeTrade.OUT);
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
                WriteMessage("Обновлены уведомления", MessageType.Timer);
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
            var authLogin = new UserLogin(cfg.SteamLogin, cfg.SteamPassword);
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
                    if (i != 0)
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

        public void MassUpdate()
        {
            try
            {
                string data = "";
                foreach (var itm in Items)
                {
                    data += itm.id + ",";
                }
                data = data.Remove(data.Length - 1);
                var ans = CLIENT1.MassInfo(cfg.key, data);

                if (ans?.Success == true)
                {
                    foreach (var itm in ans.Results)
                    {
                        double Averange, MinPrice, Discount, Cost;
                        Discount = 1 - Convert.ToDouble(cfg.discount / 100);

                        var id = itm.Classid + "_" + itm.Instanceid;
                        var FindedItem = Items.Find(item => item.id == id);

                        MinPrice = itm.SellOffers.BestOffer;

                        Averange = Convert.ToDouble(CLIENT.GetAverangePrice(FindedItem, cfg.key));

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
                        if (FindedItem.profit != 0)
                        {
                            profit = FindedItem.profit;
                        }
                        else
                        {
                            profit = cfg.price;
                        }

                        if (Cost > 20000)
                        {
                            FindedItem.price = Math.Round((Cost * Discount - profit * 2) / 100.0, 2);
                            continue;
                        }
                        if (Cost > 10000)
                        {
                            FindedItem.price = Math.Round((Cost * Discount - profit) / 100.0, 2);
                            continue;
                        }
                        else
                        {
                            FindedItem.price = Math.Round((Cost * Discount - profit) / 100.0, 2);
                            continue;
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                WriteMessage("MassUpdate: " + ex.Message, MessageType.Error);
            }
        }

		public void Dispose()
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
	}
}
