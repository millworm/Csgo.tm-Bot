using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using MonoTM2.Classes;
using MonoTM2.Const;
using MonoTM2.InputOutput;
using MonoTM2.Steam;
using Newtonsoft.Json.Linq;
using WebSocketSharp;

namespace MonoTM2
{
    public class CBot : IDisposable
    {
        bool autoConfirmTrades, Close, accountFileExist;

        //конфиг
        Config cfg;
        TradeWorker tradeWorker;

        Thread Find;

        private WebSocket client;

        const string socketHost = "wss://wsn.dota2.net/wsn/";
        private Dictionary<string, List<Itm>> Items = new Dictionary<string, List<Itm>>();

        private Action<string> UpdatePriceDelegateAction, UpdateNotificationsDelegate, CheckNotificationsDelegate;

        System.Action QuickOrderAction, UpdateOrdersDelegate;

        AsyncCallback CallbackUpdater;

        private Dictionary<string, System.Timers.Timer> Timers = new Dictionary<string, System.Timers.Timer>();

        System.Timers.Timer pinPongTimer = new System.Timers.Timer();

        public void Loader()
        {
            // AcceptTradeAction = AcceptTrades;
            //загрузка конфига
            cfg = Config.GetConfig();

            if (!String.IsNullOrEmpty(cfg.SteamSettings.Login))
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
#if !DEBUG
             tradeWorker = new TradeWorker();
            //закомментить, если не нужно автовыставление
            //работает еще в демо режиме
              tradeWorker.OnOutgoingTrade += SetItems;
            // tradeWorker.OnIncomingTrade += CheckOffers;
#endif
            UpdatePriceDelegateAction = MassUpdate;
            //UpdatePriceDelegateAction = CorrectPrice;

            UpdateOrdersDelegate = CorrectOrders;
            UpdateNotificationsDelegate = CorrectNotifications;
            QuickOrderAction = QuickOrderF;
            CheckNotificationsDelegate = CheckNotifications;

            //загрузка итемов
            if (File.Exists("items.json"))
                Items = JsonConvert.DeserializeObject<Dictionary<string, List<Itm>>>(File.ReadAllText("items.json"));

            if (Items.Count < 3)
            {
                if (!Items.ContainsKey(Host.CSGO))
                    Items.Add(Host.CSGO, new List<Itm>());

                if (!Items.ContainsKey(Host.DOTA2))
                    Items.Add(Host.DOTA2, new List<Itm>());

                if (!Items.ContainsKey(Host.PUBG))
                    Items.Add(Host.PUBG, new List<Itm>());
            }
            //таймер обновления цен
            foreach (var m in cfg.MarketsSettings)
            {
                if (m.Value.Enable)
                {
                    Timers.Add(m.Key, new System.Timers.Timer
                    {
                        Interval = m.Value.UpdatePriceTimerTime * 60 * 1000,
                    });
                    var timer = Timers.Last().Value;
                    timer.Elapsed += (sender, e) => PriceCorrectTimer(sender, e, m.Key);
                    //   timer.Site = new 
                }
            }

            Save(TypeSave.Items);

            pinPongTimer.Elapsed += PingPongTimer;
            pinPongTimer.Interval = cfg.PingPongTimerTime * 60 * 1000;

            client = new WebSocket(socketHost);

            string id = "";

            client.OnOpen += (ss, ee) =>
                    WriteMessage(string.Format("Подключение к {0} успешно ", socketHost), MessageType.Info);

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
                    var socketAnswerHost = "";
                    switch (type.type)
                    {
                        case "newitems_pb":
                        case "itemstatus_pb":
                        case "itemout_new_pb":
                            socketAnswerHost = Host.PUBG;
                            break;
                        case "newitems_cs":
                        case "itemstatus_cs":
                        case "itemout_new_cs":
                            socketAnswerHost = Host.DOTA2;
                            break;
                        case "newitems_go":
                        case "itemstatus_go":
                        case "itemout_new_go":
                            socketAnswerHost = Host.CSGO;
                            break;
                    }
                    switch (type.type)
                    {
                        case "webnotify":

                            /* answer = ee.Data.Replace("\\\"\",", "\\\",").Replace(@"\", "").Replace(" \"u0", "");
                             answer = answer.Replace(":\"{", ":{").Replace("\"\"", "");
                             answer = answer.Replace("}\"}", "}}");
                             answer = answer.Replace("\\\"\",", "\\\",");*/
                            var answerWebnotify = ee.Data
                                         .Replace("\"\\\\u", "")
                                         .Replace("\\\\\\", "\\")
                                         .Replace("\\\"", "\"")
                                         .Replace(@"\""", "\"")
                                         .Replace(":\"\"{", ":{")
                                         .Replace("\"\",", "\",")
                                         .Replace("}\"\"}", "}}");

                            if (answerWebnotify.Contains("pubg"))
                                socketAnswerHost = Host.PUBG;
                            else
                            if (answerWebnotify.Contains("dota"))
                                socketAnswerHost = Host.DOTA2;
                            else
                            if (answerWebnotify.Contains("csgo"))
                                socketAnswerHost = Host.CSGO;

                            var ansWeb = JsonConvert.DeserializeAnonymousType(answerWebnotify, webnotify);
                            //if (ansWeb != null && ansWeb.data.way == null)
                            //{
                            id = ansWeb.data.classid + "_" + ansWeb.data.instanceid;


                            var ItemInListWeb = Items[socketAnswerHost].First(itm => itm.id == id && itm.NeedBuy); //&& item.price >= (t.data.ui_price));

                            if (ItemInListWeb != null && (Functions.Buy(ItemInListWeb, socketAnswerHost, cfg.key) || Functions.Buy(ItemInListWeb, socketAnswerHost, cfg.key, 100)))
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
                        case "itemstatus_pb":
                        case "itemstatus_cs":
                            //{"type":"itemstatus_go","data":"{\"id\":\"173474064\",\"status\":5}"}
                            //WriteMessage("itemstatus_go " + ee.Data);
                            var answerItemstatus = ee.Data
                                        .Replace("\\\"", "\"")
                                        .Replace(":\"{", ":{")
                                        .Replace("}\"}", "}}");
                            var ansItem = JsonConvert.DeserializeObject<TrOf>(answerItemstatus);
                            switch (ansItem.data.status)
                            {
                                case 4:
                                    WriteMessage("Забрать вещи", MessageType.GetWeapon);
                                    if (autoConfirmTrades)
                                    {
                                        tradeWorker.AcceptTrade(TypeTrade.OUT, socketAnswerHost);
                                    }
                                    break;
                            }
                            break;
                        case "itemout_new_go":
                        case "itemout_new_cs":
                        case "itemout_new_pb":

#if DEBUG
                            System.Diagnostics.Debug.WriteLine(ee.Data);

#endif
                            //WriteMessage("itemout_new_go "+ee.Data);
                            var answerItemout = ee.Data
                                        .Replace("\\\"", "\"")
                                        .Replace(":\"{", ":{")
                                        .Replace("}\"}", "}}")
                                        .Replace(@"\", "");
                            //answer = answer.Replace(":\"\"{", ":{");
                            //answer = answer.Replace("}\"\"}", "}}");

                            var TemplItemout_new_go = new { data = new { ui_status = "" } };
                            var ans1 = JsonConvert.DeserializeAnonymousType(answerItemout, TemplItemout_new_go);
                            switch (ans1.data.ui_status)
                            {
                                case "2":
                                    WriteMessage("Передать вещи", MessageType.GiveWeapon);

                                    tradeWorker.AcceptTrade(TypeTrade.IN, socketAnswerHost);
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
                        case "newitems_pb":
                        case "newitems_cs":
                        case "newitems_go":
                            var answerNewitems = ee.Data
                                         .Replace("\\\"", "\"")
                                         .Replace(":\"{", ":{")
                                         .Replace("}\"}", "}}")
                                         .Replace("\\\"\\\\u", "")
                                         .Replace("\\\"\",", "\\\",");
                            WSItm t = JsonConvert.DeserializeObject<WSItm>(answerNewitems);

                            id = t.data.i_classid + "_" + t.data.i_instanceid;
                            var ItemInList = Items[Host.CSGO].Find(item => item.id == id && item.NeedBuy); //&& item.price >= (t.data.ui_price));

                            if (ItemInList != null && (ItemInList.price >= (t.data.ui_price)))
                            {
                                if (Functions.Buy(ItemInList, socketAnswerHost, cfg.key) || Functions.Buy(ItemInList, socketAnswerHost, cfg.key, 100))
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
#if !DEBUG
                    System.Diagnostics.Debug.WriteLine(e.Message + Environment.NewLine + ee.Data);
                    using (var sw = new System.IO.StreamWriter("sockets.txt", true))
                        sw.WriteLine($"{new string('-', 50)}\n{ee.Data}\n{new string('-', 50)}");

                   /* if (type.type != "newitems_go" && type.type != "webnotify")
                        Console.WriteLine("Error " + type.type + " " + e.Message + Environment.NewLine + Environment.NewLine  + Environment.NewLine + Environment.NewLine + ee.Data + Environment.NewLine + Environment.NewLine);*/
#endif
                }
            };

            client.OnClose += (ss, ee) =>
            {
                WriteMessage(string.Format("Отключен от {0}", socketHost), MessageType.Info);
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
            string wskey = Functions.GetWS(Host.CSGO, cfg.key);
            WS t = JsonConvert.DeserializeObject<WS>(wskey);

            client.Connect();
            if (t.error == null && t.wsAuth != null)
            {
                client.Send(t.wsAuth);
                client.Send("newitems_go");
            }
            else
                WriteMessage(t.error, MessageType.Error);
        }

        /// <summary>
        /// Поток проходящий вещи
        /// </summary>
        public void find()
        // async Task find()
        {
            while (!Close)
            {
                try
                {
                    foreach (var host in Items)
                    {
                        for (int j = 0; j < host.Value.Count; j++)
                        {
                            if (!cfg.MarketsSettings[host.Key].Enable || !host.Value[j].NeedBuy) continue;

                            if (Functions.Buy(host.Value[j], host.Key, cfg.key))
                            {
                                WriteMessage(
                                    string.Format("*find* {0} куплен. Цена на покупку ({1})", host.Value[j].name,
                                        host.Value[j].price), MessageType.BuyWeapon);
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

            foreach (var mark in cfg.MarketsSettings)
                if (mark.Value.Enable)
                    WriteMessage($"Скидка на {mark.Key} - {mark.Value.Discount}%", MessageType.Info);

            foreach (var mark in cfg.MarketsSettings)
            {
                if (mark.Value.Enable)
                {
                    MassUpdate(mark.Key);
                }
            }

            // CorrectOrders();


            WriteMessage("Цены скорректированы", MessageType.Info);

            //Говорим, что мы подключились
            foreach (var mark in cfg.MarketsSettings)
                Functions.Ping(mark.Key, cfg.key);
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
            foreach (var timer in Timers)
                timer.Value.Enabled = true;

            pinPongTimer.Enabled = true;

            WriteMessage("Таймеры включены", MessageType.Info);
        }

        /// <summary>
        /// Обновление скидки
        /// </summary>
        private void UpdateDiscount()
        {
            foreach (var mark in cfg.MarketsSettings)
            {
                if (!mark.Value.Enable) continue;

                var disc = Functions.GetDiscounts(mark.Key, cfg.key);
                if (disc != 10.01 && disc != 10.02 && disc != 10.03)
                {
                    mark.Value.Discount = disc;
                    //Config.Save(cfg);
                }
                else
                {
                    if (disc == 10.01)
                    {
                        WriteMessage("Неверный ключ\nОстановлено", MessageType.Error);
                        Console.ReadKey();
                        Environment.Exit(-1);
                    }
                }
            }
            // Config.Save();
            // WriteMessage("Скидка " + cfg.discount + " %");
        }

        /// <summary>
        /// Обновление цен
        /// </summary>
        /*void CorrectPrice()
		{
			try
			{
				foreach (Itm I in Items)
				{
					//Если предмет добавлен с уведомлений, то его цену проверяем в списке уведомлений
					if (I.priceCheck == PriceCheck.Notification)
					{
						var ans = Functions.GetNotifications(cfg.key);
						if (ans != null && ans.success)
						{
							var item = ans.dataResult.Find(fi => fi.i_classid + "_" + fi.i_instanceid == I.id);
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
						UpdateItemPrice(I);
					}
				}

				WriteMessage("Обновлены цены", MessageType.Info);
			}
			catch (Exception ex)
			{
				WriteMessage(string.Format("[CorrectPrice] {0}", ex.Message), MessageType.Error);
			}
		}*/

        /// <summary>
        /// быстрые покупки
        /// </summary>
        void QuickOrderF()
        {
            foreach (var mark in cfg.MarketsSettings)
            {
                if (!mark.Value.Enable) continue;
                //получаем список вещей
                var QuickItemsList = Functions.QList(mark.Key, cfg.key);

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
                            i => i.Value.Any(j => j.id == q.i_classid + "_" + q.i_instanceid &&
                                 Convert.ToDouble(q.l_paid) < j.price * 100 &&
                                 j.NeedBuy)))
                    .ToList();

                if (l.Count > 0)
                {
                    int i = 0;
                    l.ForEach(x =>
                    {
                        if (Functions.QBuy(mark.Key, cfg.key, x.ui_id))
                        {
                            i++;
                        }
                    });
                    if (i != 0)
                    {
                        WriteMessage("*Список* Куплен предмет", MessageType.BuyWeapon);
                        if (autoConfirmTrades)
                            tradeWorker.AcceptTrade(TypeTrade.OUT, mark.Key);
                    }
                }
            }

        }

        /// <summary>
        /// Сохраняет настройки и итемы в файл
        /// </summary>
        /// <param name="S">true сохраняет настройки. false сохраняет итемы</param>
        void Save(TypeSave type)
        {
            //сохраняем настройки
            switch (type)
            {
                case TypeSave.Config:
                    Config.Save();
                    break;
                case TypeSave.Items:
                    File.WriteAllText("items.json", JsonConvert.SerializeObject(Items, Formatting.Indented));

                    WriteMessage("Список сохранен", MessageType.Info);
                    break;
            }
        }

        /// <summary>
        /// Обновление цен и проверка обменов
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void PriceCorrectTimer(object sender, ElapsedEventArgs elapsedEventArgs, string host)
        {
            WriteMessage($"[{host}]Таймер", MessageType.Timer);
            //выставитьToolStripMenuItem_Click(sender, e);
            UpdatePriceDelegateAction.BeginInvoke(host, null, null);

            if (autoConfirmTrades)
            {
                var info = Functions.CountItemsToTransfer(host, cfg.key);
                if (info?.outCount > 0)
                    tradeWorker.AcceptTrade(TypeTrade.OUT, host);
                if (info?.inCount > 0)
                    tradeWorker.AcceptTrade(TypeTrade.IN, host);
                //AcceptTradeAction.Invoke();
                //AcceptMobileOrdersAction.Invoke();
                SetItems(host, null);
            }
        }

        /// <summary>
        /// Говорим сайту что мы онлайн и проверям быстрые покупки
        /// </summary>
        void PingPongTimer(object sender, ElapsedEventArgs e)
        {
            foreach (var mark in cfg.MarketsSettings)
            {
                if (!mark.Value.Enable) continue;
                Functions.Ping(mark.Key, cfg.key);
                //
                client.Send("PING");
                //проверка быстрых покупок
                QuickOrderAction.Invoke();

                if (autoConfirmTrades)
                {
                    CheckOffers(mark.Key, e);
                }
            }
        }


        /// <summary>
        /// подтвердить трейд
        /// </summary>
        public void ConfirmTrade()
        {
            //tradeWorker.AcceptTrade(TypeTrade.OUT);
        }

        /// <summary>
        /// Добавление предмета в список покупок
        /// </summary>
        /// <param name="link">ссылка на страницу с предметом</param>
        public void AddItem(string link, int _price = 0, PriceCheck _check = PriceCheck.Price)
        {
            var domain = Regex.Matches(link, @"(http.?\:\/\/[a-z0-9.]*)");
            var host = domain[0].Value;
            switch (host)
            {
                case Host.CSGO:
                case Host.DOTA2:
                case Host.PUBG:
                    break;
                default:
                    WriteMessage("Ошибка в домене", MessageType.Error);
                    return;
            }

            var itm = new Itm
            {
                link = link,
                price = _price,
                priceCheck = _check
            };

            itm.hash = Functions.GetHash(itm, host, cfg.key);
            //проверяем, нет ли этого предмета в списке
            var ch = Items[host].Find(p => p.id == itm.id);
            if (ch != null)
            {
                WriteMessage("Данный предмет уже есть", MessageType.Info);
                return;
            }

            if (itm.name != "")
            {
                Items[host].Add(itm);
                WriteMessage($"Добавлен {itm.name}\nЦена {itm.price} рублей", MessageType.Info);
                Save(TypeSave.Items);
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
            //UpdatePriceDelegateAction.BeginInvoke(null, null);
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
        public int GetPrice(string host)
        {
            return cfg.MarketsSettings[host].Profit;
        }
        /// <summary>
        /// Установить прибыль
        /// </summary>
        /// <param name="newPrice">Прибыль в копейках</param>
        public void SetPrice(string newPrice, string host)
        {
            try
            {
                cfg.MarketsSettings[host].Profit = Convert.ToInt16(newPrice);
                Save(TypeSave.Config);
            }
            catch (Exception ex)
            {
                WriteMessage(string.Format("Ошибка в выставлении прибыли/r/nМетод {0}/r/nОшибка:{1}", System.Reflection.MethodInfo.GetCurrentMethod().Name, ex.Message), MessageType.Error);
            }
        }

        public Dictionary<string, List<Itm>> GetListItems()
        {
            return Items;
        }

        public Itm GetItem(int index, string host)
        {
            return Items[host][index];
        }

        public void RemoveItem(int index, string host)
        {
            //снятие ордера
            Items[host][index].price = 0;
            Functions.ProcessOrder(Items[host][index], host, cfg.key);
            //удаление уведомления
            Functions.Notification(Items[host][index], host, cfg.key, true);
            ///удаление предмета из списка
            Items[host].RemoveAt(index);
            //сохранение изменений
            Save(TypeSave.Items);
        }


        public void SetUpdateTimer(string time, string host)
        {
            cfg.MarketsSettings[host].UpdatePriceTimerTime = Convert.ToInt16(time);
            Config.Save();

            Timers[host].Stop();
            Timers[host].Interval = cfg.MarketsSettings[host].UpdatePriceTimerTime * 60 * 1000;
            Timers[host].Start();
        }

        public double GetUpdateTimer(string host)
        {
            return cfg.MarketsSettings[host].UpdatePriceTimerTime;
        }
        public void AcceptMobileOrdersFunc()
        {
            foreach (var host in Items)
                tradeWorker.AcceptTrade(TypeTrade.IN, host.Key);
        }
        public void AcceptMobileOrders()
        {
            foreach (var host in Items)
                tradeWorker.AcceptTrade(TypeTrade.MOBILE, host.Key);
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
            //tradeWorker.AcceptTrade(TypeTrade.OUT);
        }

        /// <summary>
        /// Запустить обновление рдеров
        /// </summary>
        public void UpdateOrdersAndNotifications()
        {
            //UpdateOrdersDelegate.Invoke();
            foreach (var host in Items)
                UpdateNotificationsDelegate.BeginInvoke(host.Key, null, null);
        }

        /// <summary>
        /// Выставление оредеров автопокупок
        /// </summary>
        private void CorrectOrders()
        {
            try
            {
                string ans;
                foreach (var host in Items)
                {
                    foreach (var item in host.Value)
                    {
                        if (!item.NeedBuy) continue;
                        ans = Functions.ProcessOrder(item, host.Key, cfg.key);
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
            }
            catch (Exception ex)
            {
                WriteMessage(string.Format("[Correctorders] {0}", ex.Message), MessageType.Error);
            }
        }

        void CorrectOrdersAndNotifications(string host)
        {
            // UpdateOrdersDelegate.BeginInvoke(null, null);
            UpdateNotificationsDelegate.Invoke(host);
            CheckNotificationsDelegate.Invoke(host);
        }

        /// <summary>
        /// Обновление уведомлений
        /// </summary>
        void CorrectNotifications(string host)
        {
            ReturnResult<bool> answer = null;
            try
            {
                for (int i = 0; i < Items[host].Count; i++)
                {
                    if (!Items[host][i].NeedBuy) continue;

                    answer = Functions.Notification(Items[host][i], host, cfg.key, false);

                    if (answer.success)
                        Thread.Sleep(cfg.Itimer);
                }

                WriteMessage($"Обновлены уведомления {host}", MessageType.Timer);
            }
            catch (Exception ex)
            {
                WriteMessage($"[CorrectNotifications] {ex.Message} {answer.errorMessage}", MessageType.Error);
            }
        }

        /// <summary>
        /// Вывод сообщения в консоль
        /// </summary>
        /// <param name="msg">Текст</param>
        /// <param name="msgType">Тип сообщения</param>
        void WriteMessage(string msg, MessageType msgType)
        {
            if (cfg.IsEnabledMessage(msgType))
                ConsoleInputOutput.OutputMessage(msg, msgType);
        }

        public string Status()
        {
            //if (Find != null)
            return string.Format("Find {0}", Find.ThreadState);
            //else
            //return string.Format("Find {0}", Finding.Status);
        }



        public string GetProfit()
        {
            StringBuilder sb = new StringBuilder();
            foreach (var host in cfg.MarketsSettings)
            {
                if (!cfg.MarketsSettings[host.Key].Enable) continue;
                var p = Functions.GetProfit(host.Key, cfg.key);
                if (p != -1 && p != -2)
                {
                    sb.AppendLine($"Получено за день на {host.Key} - {p / 100.0} рублей");
                }
                else
                {
                    sb.AppendLine($"Ошибка {host.Key}");
                }
            }
            return sb.ToString();
        }


        //

        /// <summary>
        /// Проверяем, есть ли предметы в уведомлениях, которые нужно добавить в покупки
        /// </summary>
        public void CheckNotifications(string host)
        {
            try
            {
                int i = 0;
                var ans = Functions.GetNotifications(host, cfg.key);
                if (ans.success)
                {
                    foreach (var itm in ans.dataResult)
                    {
                        var haveInItems = Items[host].Find(item => item.id == itm.i_classid + "_" + itm.i_instanceid);
                        if (haveInItems == null)
                        {
                            AddItem($"{host}/{itm.i_classid}-{itm.i_instanceid}", Convert.ToInt32(itm.n_val) / 100, PriceCheck.Notification);
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
        /// <param name="id">Номер предмета в списке</param>
        /// <param name="profit">Прибыль</param>
        /// <returns></returns>
        public bool SetProfit(string host, int id, int profit)
        {
            try
            {
                Items[host][id].profit = profit;
                Save(TypeSave.Items);
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
        /// <param name="id">Номер предмета</param>
        /// <returns></returns>
        public PriceCheck GetPriceCheck(string host, int id)
        {
            return Items[host][id].priceCheck;
        }

        /// <summary>
        /// Установить тип проверки цены
        /// </summary>
        /// <param name="id">Номер предмета</param>
        /// <param name="type">Тип</param>
        /// <returns></returns>
        public bool SetPriceCheck(string host, int id, int type)
        {
            try
            {
                var typePriceCheck = (PriceCheck)type;
                Items[host][id].priceCheck = typePriceCheck;
                Save(TypeSave.Items);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Обновление цен за один проход
        /// </summary>
        private void MassUpdate(string host)
        {
            var data = new StringBuilder();

            var items = Items[host];
            if (items.Count == 0 || !cfg.MarketsSettings[host].Enable) return;

            int iterationsCount = items.Count / 100;
            for (int j = 0; j <= iterationsCount; j++)
            {
                data.Clear();

                var limit = items.Count < 100 ? items.Count : 2 * j * 100;

                for (int i = j * 100; i < limit; i++)
                {
                    if (!items[i].NeedBuy) continue;

                    data.Append(items[i].id);
                    data.Append(",");
                }
                if (data.Length == 0) continue;
                data.Remove(data.Length - 1, 1);

                var ans = Functions.MassInfo(host, cfg.key, data.ToString());

                if (!ans.success) ans = Functions.MassInfo(host, cfg.key, data.ToString());

                if (ans.success)
                {
                    foreach (var itm in ans.dataResult)
                    {
                        var id = itm.classid + "_" + itm.instanceid;
                        var findedItem = items.Find(item => item.id == id);

                        if (findedItem.priceCheck == PriceCheck.Notification)
                        {
                            var ansNotif = Functions.GetNotifications(host, cfg.key);
                            if (ansNotif.success)
                            {
                                var item = ansNotif.dataResult.Find(
                                    fi => fi.i_classid + "_" + fi.i_instanceid == findedItem.id);
                                if (item != null)
                                {
                                    findedItem.price = Convert.ToInt32(item.n_val) / 100.0;
                                }
                            }
                        }
                        else
                        {
                            UpdateItemPrice(host, findedItem, itm.sellOffers?.bestOffer);
                        }
                    }
                }
                else
                {
                    if (ans.errorMessage != null)
                        WriteMessage(ans.errorMessage, MessageType.Error);
                }
            }

            CorrectOrdersAndNotifications(host);
        }

        private void UpdateItemPrice(string host, Itm itm, int? minPrice = null)
        {
            var discount = 1 - Convert.ToDouble(cfg.MarketsSettings[host].Discount / 100);

            if (minPrice == null)
            {
                minPrice = Functions.GetMinPrice(itm, host, cfg.key);
            }

            int averangePrice = Functions.GetAverangePrice(itm, host, cfg.key);

            int cost = averangePrice < minPrice ? averangePrice : (int)minPrice;

            //если у предмета выставлена персональная прибыль, то используем ее, а не общую
            double profit;
            if (itm.profit != 0)
            {
                profit = itm.profit;
            }
            else
            {
                profit = cfg.MarketsSettings[host].Profit;
            }

            if (cost > 20000)
            {
                itm.price = Math.Round((cost * discount - profit * 2) / 100.0, 2);
                return;
            }
            if (cost > 10000)
            {
                itm.price = Math.Round((cost * discount - profit) / 100.0, 2);
            }
            else
            {
                itm.price = Math.Round((cost * discount - profit) / 100.0, 2);
            }
        }

        /// <summary>
        /// Выставление вещей на продажу
        /// </summary>
	    public void SetItems(object sender, EventArgs e)
        {
            
            var host = (string)sender;

            Functions.UpdateInvent(host, cfg.key);
            Thread.Sleep(3000);

            var setting = cfg.MarketsSettings[host];
            //Получаем список вещей в инвентаре
            var inv = Functions.GetInv(host, cfg.key);
            inv = !inv.success && inv.errorMessage.IndexOf("обновите", StringComparison.InvariantCultureIgnoreCase) == -1 ? inv : Functions.GetInv(host, cfg.key);
            if (inv.success && inv.dataResult.Count > 0)
            {
               var buyPlace = Functions.GetPlace(host, "buy");

                var discount = 1 - Convert.ToDouble(setting.Discount / 100);
                //double profit;
                var history = Functions.OperationHistory(DateTimeOffset.Now.AddHours(-4).ToUnixTimeSeconds(),
                    DateTimeOffset.Now.AddHours(+3).ToUnixTimeSeconds(), host, cfg.key);

                foreach (var itm in inv.dataResult)
                {
                    var itemFromItemList = Items[host].Find(fnd => fnd.id == $"{itm.i_classid}_{itm.i_instanceid}");
                    var buyList = history.dataResult.FindAll(item =>
                        item.stage == "2"
                        && item.h_event == buyPlace
                        && (item.classid == itm.i_classid && item.instanceid == itm.i_instanceid
                            || item.market_hash_name == itm.i_market_hash_name));

                    //если в истории не нашли покупку, то выходим
                    if (buyList.Count == 0 && 
                        itemFromItemList.NeedBuy && 
                        itemFromItemList.SetItemType == TypeForSetItem.Auto) continue;

                    ////получаем прибыль
                    //var itemFromBuyList = Items.Find(i => i.id == $"{itm.i_classid}_{itm.i_instanceid}");
                    //if (itemFromBuyList?.profit != 0)
                    //{
                    //    profit = itemFromBuyList.profit;
                    //}
                    //else
                    //{
                    //    profit = cfg.price;
                    //}

                    var buyPrice = buyList.Max(item => double.Parse(item.paid));

                    //Получаем предложения о продаже
                    var sellOffers = Functions.SellOffers(buyList[0].classid, buyList[0].instanceid, host, cfg.key);

                    if (!sellOffers.success) continue;
                    int priceForSet = 0;

                    switch (itemFromItemList.SetItemType)
                    {
                        case TypeForSetItem.Auto:
                            priceForSet = getPriceForSet(new Itm {id = $"{buyList[0].classid}_{buyList[0].instanceid}"},
                                sellOffers.dataResult,
                                buyPrice);
                            break;
                        case TypeForSetItem.Manual:
                            priceForSet = itemFromItemList.PriceForSetItem;
                            break;
                    }

                    // var recievedMoney = (int)(priceForSet * discount - profit);
                    var recievedMoney = (int)(priceForSet * discount);
                    if (recievedMoney > buyPrice)
                    {
                        Functions.SetPrice(itm, priceForSet, host, cfg.key);
                        WriteMessage($"Выставлен {itm.i_market_hash_name} за {priceForSet} коп.", MessageType.Info);
                    }
                    else
                    {
                        WriteMessage($"Куплен за {buyPrice}\r\nцена для выставления {priceForSet}\r\nполучено {recievedMoney}", MessageType.Info);
                    }
                }
            }
            else
            {
                if (inv.errorMessage != null)
                {
                    WriteMessage(inv.errorMessage, MessageType.Error);
                    if (inv.errorMessage.IndexOf("обновите", StringComparison.InvariantCultureIgnoreCase) != -1 || inv.errorMessage.IndexOf("виден", StringComparison.InvariantCultureIgnoreCase) != -1)
                        Functions.UpdateInvent(host, cfg.key);
                }
            }
        }

        int getPriceForSet(Itm item, IReadOnlyList<Offer> sellOffers, double buyPrice, int maxItemsPerOfferPrice = 2, int maxPosition = 5)
        {
            var position = 0;
            //Максимальная цена: ~ средняя + 10%
            //чтобы сильно не уйти, если список продаж пустой
            /* var exPrice =
                 (int)(Functions.GetAverangePrice(item,
                           cfg.key) * 1.1 + 0.5);*/
            int i = 0;
            for (; i < sellOffers.Count; i++)
            {
                var valuePos = int.Parse(sellOffers[i].count);
                var valuePrice = int.Parse(sellOffers[i].price);

                if (valuePos > maxItemsPerOfferPrice) break; //exPrice < valuePrice ||

                if (position + valuePos < maxPosition && valuePrice > buyPrice)
                    position += valuePos;
                else
                {
                    break;
                }
            }

            int priceForSet = int.Parse(sellOffers[i].price) - 1;

            // if (exPrice < priceForSet)
            //   priceForSet = exPrice;
            return priceForSet;
        }

        private void CheckOffers(object sender, EventArgs e)
        {
            var host = (string)sender;
            var info = Functions.CountItemsToTransfer(host, cfg.key);

            CurrentState.IncomingTradeCount = info?.inCount;
            CurrentState.OutgoingTradeCount = info?.outCount;

            if (CurrentState.OutgoingTradeCount > 0)
            {
                tradeWorker.AcceptTrade(TypeTrade.OUT, host);
            }
            if (CurrentState.IncomingTradeCount > 0)
            {
                tradeWorker.AcceptTrade(TypeTrade.IN, host);
            }
        }

        public void Dispose()
        {
            Close = true;

            foreach (var timer in Timers)
            {
                timer.Value.Stop();
                timer.Value.Dispose();
            }

            pinPongTimer.Stop();
            pinPongTimer.Dispose();

            client.Close();


            foreach (var host in Items)
            {
                Functions.DeleteAllOrders(host.Key, cfg.key);


                for (var i = 0; i < host.Value.Count; i++)
                {
                    RemoveItem(i, host.Key);
                    Thread.Sleep(cfg.Itimer);
                }

                Functions.GoOffline(host.Key, cfg.key);

            }
        }

        public void CreateAuth()
        {
            Mobile.CreateAuth();
        }

        public void GenerateGuardCode()
        {
            Mobile.GenerateGuardCode();
        }
    }
}
