using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Timers;
using MonoTM2.Classes;
using MonoTM2.InputOutput;
using MonoTM2.Steam;
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
            //загрузка конфига
            cfg = Config.GetConfig();

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
				//Config.Save();

				ConsoleInputOutput.OutputMessage("Заполните config.json для автоматического получения вещей");
			}
#if !DEBUG
            tradeWorker = new TradeWorker();
            //закомментить, если не нужно автовыставление
            //работает еще в демо режиме
            tradeWorker.OnOutgoingTrade += SetItems;
		    tradeWorker.OnIncomingTrade += CheckOffers;
#endif
            CallbackUpdater = CorrectOrdersAndNotifications;
			if (cfg.MassUpdate)
				UpdatePriceDelegateAction = MassUpdate;
			else
				UpdatePriceDelegateAction = CorrectPrice;

			UpdateOrdersDelegate = CorrectOrders;
			UpdateNotificationsDelegate = CorrectNotifications;
			QuickOrderAction = QuickOrderF;
			CheckNotificationsDelegate = CheckNotifications;

			//загрузка итемов
			FileInfo b = new FileInfo("csgotmitems.dat");
			if (b.Exists)
			{
				FileStream fstream = File.Open("csgotmitems.dat", FileMode.Open);
			    BinaryFormatter binaryFormatter = new BinaryFormatter();
				Items = (List<Itm>)binaryFormatter.Deserialize(fstream);
				fstream.Close();
				Items.Sort((i1, i2) => i1.name.CompareTo(i2.name));
			    Items.ForEach(i => i.price = 0);
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

				Config.Save();
			}

			//таймер обновления цен
			UpdatePriceTimer.Elapsed += PriceCorrectTimer;
			UpdatePriceTimer.Interval = cfg.UpdatePriceTimerTime * 60 * 1000;

			pinPongTimer.Elapsed += PingPongTimer;
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
							answer = ee.Data
										.Replace("\"\\\\u", "")
										.Replace("\\\\\\", "\\")
										.Replace("\\\"", "\"")
										.Replace(@"\""", "\"")
										.Replace(":\"\"{", ":{")
										.Replace("\"\",", "\",")
										.Replace("}\"\"}", "}}");

							var ansWeb = JsonConvert.DeserializeAnonymousType(answer, webnotify);
							//if (ansWeb != null && ansWeb.data.way == null)
							//{
							id = ansWeb.data.classid + "_" + ansWeb.data.instanceid;
							var ItemInListWeb = Items.Find(item => item.id == id); //&& item.price >= (t.data.ui_price));

							if (ItemInListWeb != null && (Functions.Buy(ItemInListWeb, cfg.key) || Functions.Buy(ItemInListWeb, cfg.key, 100)))
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
							answer = ee.Data
										.Replace("\\\"", "\"")
										.Replace(":\"{", ":{")
										.Replace("}\"}", "}}");
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
							answer = ee.Data
										.Replace("\\\"", "\"")
										.Replace(":\"{", ":{")
										.Replace("}\"}", "}}");
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
							answer = ee.Data
										.Replace("\\\"", "\"")
										.Replace(":\"{", ":{")
										.Replace("}\"}", "}}")
										.Replace("\\\"\\\\u", "")
										.Replace("\\\"\",", "\\\",");
							WSItm t = JsonConvert.DeserializeObject<WSItm>(answer);

							id = t.data.i_classid + "_" + t.data.i_instanceid;
							var ItemInList = Items.Find(item => item.id == id); //&& item.price >= (t.data.ui_price));

							if (ItemInList != null && (ItemInList.price >= (t.data.ui_price)))
							{
								if (Functions.Buy(ItemInList, cfg.key) || Functions.Buy(ItemInList, cfg.key, 100))
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
			string wskey = Functions.GetWS(cfg.key);
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
					for (int i = 0; i < Items.Count; i++)
					{
						{
							if (Functions.Buy(Items[i], cfg.key))
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

            //Говорим, что мы подключились
		    Functions.Ping(cfg.key);
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
			var disc = Functions.GetDiscounts(cfg.key);
			if (disc != 10.01 && disc != 10.02 && disc != 10.03)
			{
				cfg.discount = disc;
				//Config.Save(cfg);
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
			    double Discount = 1 - Convert.ToDouble(cfg.discount / 100);
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
		}

		/// <summary>
		/// быстрые покупки
		/// </summary>
		void QuickOrderF()
		{
			//получаем список вещей
			var QuickItemsList = Functions.QList(cfg.key);

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
					if (Functions.QBuy(cfg.key, x.ui_id))
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
				Config.Save();
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
				var info = Functions.CountItemsToTransfer(cfg.key);
				if (info?.getCount > 0)
					tradeWorker.AcceptTrade(TypeTrade.OUT);
				if (info?.outCount > 0)
					tradeWorker.AcceptTrade(TypeTrade.IN);
				//AcceptTradeAction.Invoke();
				//AcceptMobileOrdersAction.Invoke();
			    SetItems(this, null);
			}
		}

		/// <summary>
		/// Говорим сайту что мы онлайн и проверям быстрые покупки
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void PingPongTimer(object sender, ElapsedEventArgs e)
		{
		    Functions.Ping(cfg.key);
			//
			client.Send("PING");
			//проверка быстрых покупок
			QuickOrderAction.Invoke();

			if (autoConfirmTrades)
			{
			    CheckOffers(sender, e);
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

			itm.hash = Functions.GetHash(itm, cfg.key);
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
		    Functions.ProcessOrder(Items[index], cfg.key);
            //удаление уведомления
		    Functions.Notification(Items[index], cfg.key,true);
			///удаление предмета из списка
			Items.RemoveAt(index);
			//сохранение изменений
			Save(false);
		}


		public void SetUpdateTimer(string time)
		{
			cfg.UpdatePriceTimerTime = Convert.ToInt16(time);
		    Config.Save();
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
					ans = Functions.ProcessOrder(I, cfg.key);
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
		    ReturnResult<bool> answer = null;
		    try
			{
			    for (int i = 0; i < Items.Count; i++)
			    {
			        answer = Functions.Notification(Items[i], cfg.key,false);

			        if (answer.success)
			            Thread.Sleep(cfg.Itimer);
			    }
				WriteMessage("Обновлены уведомления", MessageType.Timer);
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
			var p = Functions.GetProfit(cfg.key);
			if (p != -1 && p != -2)
			{
				return ("Получено за день " + p / 100.0 + " рублей");
			}
			else
			{
				return ("Ошибка");
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
				var ans = Functions.GetNotifications(cfg.key);
				if (ans.success)
				{
					foreach (var itm in ans.dataResult)
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
		/// <param name="id">Номер предмета в списке</param>
		/// <param name="profit">Прибыль</param>
		/// <returns></returns>
		public bool SetProfit(int id, int profit)
		{
			try
			{
				Items[id].profit = profit;
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
		/// <param name="id">Номер предмета</param>
		/// <returns></returns>
		public PriceCheck GetPriceCheck(int id)
		{
			return Items[id].priceCheck;
		}

		/// <summary>
		/// Установить тип проверки цены
		/// </summary>
		/// <param name="id">Номер предмета</param>
		/// <param name="type">Тип</param>
		/// <returns></returns>
		public bool SetPriceCheck(int id, int type)
		{
			try
			{
				var typePriceCheck = (PriceCheck)type;
				Items[id].priceCheck = typePriceCheck;
				Save(false);
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
		private void MassUpdate()
		{
		    int iterationsCount = Items.Count / 100;
		    var data = new StringBuilder();
		    for (int j = 0; j <= iterationsCount; j++)
		    {
		        data.Clear();
                var limit = Items.Count < 100 ? Items.Count : 2 * j * 100;

                for (int i = j * 100; i < limit; i++)
		        {
		            data.Append(Items[i].id);
		            data.Append(",");
                }

		        data.Remove(data.Length - 1, 1);

		        var ans = Functions.MassInfo(cfg.key, data.ToString());

		        if (!ans.success) ans = Functions.MassInfo(cfg.key, data.ToString());

		        if (ans.success)
		        {
		            foreach (var itm in ans.dataResult)
		            {
		                var id = itm.classid + "_" + itm.instanceid;
		                var findedItem = Items.Find(item => item.id == id);

		                if (findedItem.priceCheck == PriceCheck.Notification)
		                {
		                    var ansNotif = Functions.GetNotifications(cfg.key);
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
		                    UpdateItemPrice(findedItem, itm.sellOffers?.bestOffer);
		                }
		            }
		        }
		        else
		        {
		            if (ans.errorMessage != null)
		                WriteMessage(ans.errorMessage, MessageType.Error);
		        }
		    }
		}

		private void UpdateItemPrice(Itm itm,  int? minPrice = null)
		{
			var discount = 1 - Convert.ToDouble(cfg.discount / 100);

			if (minPrice == null)
			{
				minPrice = Functions.GetMinPrice(itm, cfg.key);
			}

			int averangePrice = Functions.GetAverangePrice(itm, cfg.key);

			int cost = averangePrice < minPrice ? averangePrice : (int)minPrice;

			//если у предмета выставлена персональная прибыль, то используем ее, а не общую
			double profit;
			if (itm.profit != 0)
			{
				profit = itm.profit;
			}
			else
			{
				profit = cfg.price;
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
        /// <param name="maxItemsPerOfferPrice">Максимальное число вещей за одну цену</param>
        /// <param name="maxPosition">Максимальная позиция для выставления</param>
	    public void SetItems(object sender, EventArgs e)
        {
            //Получаем список вещей в инвентаре
            var inv = Functions.GetInv(cfg.key);
            inv = !inv.success && inv.errorMessage.IndexOf("обновите", StringComparison.InvariantCultureIgnoreCase) == -1 ? inv : Functions.GetInv(cfg.key);
            if (inv.success && inv.dataResult.Count > 0)
            {
                var discount = 1 - Convert.ToDouble(cfg.discount / 100);
                //double profit;
                var history = Functions.OperationHistory(DateTimeOffset.Now.AddHours(-4).ToUnixTimeSeconds(),
                    DateTimeOffset.Now.AddHours(+3).ToUnixTimeSeconds(), cfg.key);

                foreach (var itm in inv.dataResult)
                {
                    var buyList = history.dataResult.FindAll(item =>
                        item.stage == "2"
                        && item.h_event == "buy_go"
                        && (item.classid == itm.i_classid && item.instanceid == itm.i_instanceid 
                            || item.market_hash_name == itm.i_market_hash_name));

                    //если в истории не нашли покупку, то выходим
                    if (buyList.Count == 0) continue;

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
                    var sellOffers = Functions.SellOffers(buyList[0].classid, buyList[0].instanceid, cfg.key);

                    if (!sellOffers.success) continue;
                    var priceForSet = getPriceForSet(
                        new Itm {id = $"{buyList[0].classid}_{buyList[0].instanceid}"},
                        sellOffers.dataResult,
                        buyPrice);

                    // var recievedMoney = (int)(priceForSet * discount - profit);
                    var recievedMoney = (int)(priceForSet * discount);
                    if (recievedMoney > buyPrice)
                    {
                        Functions.SetPrice(itm, priceForSet, cfg.key);
                        WriteMessage($"Выставлен {itm.i_market_hash_name} за {priceForSet} коп.", MessageType.Info);
                    }
                    else
                        if (sender != null && sender.Equals("Console"))
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
                    if (inv.errorMessage.IndexOf("обновите", StringComparison.InvariantCultureIgnoreCase) != -1)
                        Functions.UpdateInvent(cfg.key);
                }
            }
        }

        int getPriceForSet(Itm item, IReadOnlyList<Offer> sellOffers, double buyPrice, int maxItemsPerOfferPrice = 2, int maxPosition = 5)
        {
            var position = 0;
            //Максимальная цена: ~ средняя + 10%
            //чтобы сильно не уйти, если список продаж пустой
            var exPrice =
                (int)(Functions.GetAverangePrice(item,
                          cfg.key) * 1.1 + 0.5);
            int i = 0;
            for (; i < sellOffers.Count; i++)
            {
                var valuePos = int.Parse(sellOffers[i].count);
                var valuePrice = int.Parse(sellOffers[i].price);

                if (exPrice < valuePrice || valuePos > maxItemsPerOfferPrice) break;

                if (position + valuePos < maxPosition && valuePrice > buyPrice)
                    position += valuePos;
                else
                {
                    break;
                }
            }

            int priceForSet = int.Parse(sellOffers[i].price) - 1;

            if (exPrice < priceForSet)
                priceForSet = exPrice;
            return priceForSet;
        }

	    private void CheckOffers(object sender, EventArgs e)
	    {
	        var info = Functions.CountItemsToTransfer(cfg.key);
	        if (info?.getCount > 0)
	        {
	            tradeWorker.AcceptTrade(TypeTrade.OUT);
	        }
	        if (info?.outCount > 0)
	        {
	            tradeWorker.AcceptTrade(TypeTrade.IN);
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

		    Functions.DeleteAllOrders(cfg.key);

            for (var i = 0; i < Items.Count; i++)
            {
                RemoveItem(i);
                Thread.Sleep(cfg.Itimer);
            }

            Functions.GoOffline(cfg.key);
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
