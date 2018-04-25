using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Text;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using MonoTM2.Classes;
using MonoTM2.Const;
using MonoTM2.InputOutput;

namespace MonoTM2
{
    class Functions
    {
        private static readonly string _line = new string('-', 50);

        //поиск вещи
        public static bool Buy(Itm item, string host, string key, int timeout = 500)
        {
            int i = 0;
            try
            {
                a:
                //string resp = Web($"{host}/api/Buy/" + item.id + "/" + Convert.ToInt32(item.price * 100) + "/" + item.hash + "/" + "?key={key}", timeout);
                string resp = Web($"{host}/api/Buy/{item.id}/{Convert.ToInt32(item.price * 100)}/?key={key}", timeout);
                aBuy it = JsonConvert.DeserializeObject<aBuy>(resp);
                if (it.id?.ToLower() != "false")
                {
                    if (it.result == "ok")
                    {
                        return true;
                    }
                }
                else
                {
                    if (it.error?.ToLower() == "bad key")
                    {
                        ConsoleInputOutput.OutputMessage("Неверный ключ", MessageType.Error);
                        Console.ReadKey();
                        Environment.Exit(-1);
                    }
                    if (it.result.IndexOf("изменилось", StringComparison.OrdinalIgnoreCase) != -1
                        || it.result.IndexOf("вывод", StringComparison.OrdinalIgnoreCase) != -1
                        || it.result.IndexOf("средств", StringComparison.OrdinalIgnoreCase) != -1
                        || it.result.IndexOf("инвентарь", StringComparison.OrdinalIgnoreCase) != -1
                        || it.result.IndexOf("боты", StringComparison.OrdinalIgnoreCase) != -1)
                    {
                        return false;
                    }
                    if (it.result.IndexOf("устарело", StringComparison.OrdinalIgnoreCase) == -1 && i++ < 5)
                    {
                        // Console.WriteLine(string.Format("[{0}] {1}", item.name, it.result));
                        goto a;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }

        }

        /// <summary>
        /// получить список выщей на продаже
        /// </summary>
        /// <param name="key"></param>
        /// <returns>Trades</returns>
        public static Trades GetTrades(string host, string key)
        {
            try
            {
                string resp = Web($"{host}/api/Trades/?key={key}");
                resp = resp.Insert(0, "{\"items\":");
                resp += "}";
                return JsonConvert.DeserializeObject<Trades>(resp);
            }
            catch
            {
                return new Trades();
            }
        }


        /// <summary>
        /// Получает хеш предмета и заполняет название
        /// </summary>
        /// <param name="item">предмет для которого нужно получить хеш и заполнить название</param>
        /// <param name="key">Api key</param>
        /// <param name="lang">en,ru</param>
        /// <returns>string hash</returns>
        public static string GetHash(Itm item, string host, string key, string lang = "en")
        {
            string pattern = "[0-9]{1,15}[-]{1}[0-9]{1,15}";
            var match = System.Text.RegularExpressions.Regex.Match(item.link, pattern);
            var code = match.Value.Replace("-", "_");

            string resp = Web($"{host}/api/ItemInfo/{code}/{lang}/?key={key}", 5000);
            Info inf = JsonConvert.DeserializeObject<Info>(resp);

            item.name = inf.name + " (" + inf.quality + ")";
            item.id = code;
            item.hash = inf.hash;
            return inf.hash;
        }

        /// <summary>
        /// Запрос мин. цены
        /// </summary>
        public static int GetMinPrice(Itm item, string host, string key)
        {
            string resp = Web($"{host}/api/BestSellOffer/{item.id}/?key={key}");

            var bestSellOffer = new { success = false, best_offer = "", error = "" };
            var message = JsonConvert.DeserializeAnonymousType(resp, bestSellOffer);
            return (bool)message?.success ? int.Parse(message.best_offer) : -1;
        }

        /// <summary>
        /// Запрос средней цены
        /// </summary>
        public static int GetAverangePrice(Itm item, string host, string key)
        {
            string resp = Web($"{host}/api/ItemHistory/{item.id}/?key={key}");
            var pr = new { success = false, average = 0 };
            var inf = JsonConvert.DeserializeAnonymousType(resp, pr);
            return (bool)inf?.success ? inf.average : 0;
        }

        /// <summary>
        /// Запрос последней в истории цены
        /// </summary>
        public static int GetLastPrice(Itm item, string host, string key)
        {
            string resp = Web($"{host}/api/ItemHistory/{item.id}/?key={key}");
            var pr = new { success = false, average = 0, history = new[] { new { l_price = "" } }, error = "" };
            var inf = JsonConvert.DeserializeAnonymousType(resp, pr);
            return (bool)inf?.success ? Convert.ToInt32(inf.history[0].l_price) : 0;
        }

        /// <summary>
        /// Запрос баланса
        /// </summary>
        public static int GetMoney(string host, string key)
        {
            try
            {
                string resp = Web($"{host}/api/GetMoney/?key={key}");
                ConsoleInputOutput.OutputMessage($"Баланс: {resp}", MessageType.Default);

                Money mon = JsonConvert.DeserializeObject<Money>(resp);
                if (mon.error == "Bad KEY")
                {
                    return -1;
                }
                else
                {
                    return mon.money;
                }
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Запрос оффера
        /// </summary>
        /// <param name="bid"></param>
        /// <param name="key"></param>
        /// <param name="out"></param>
        /// <returns></returns>
        public static Trade GetOffer(string bid, string host, string key, string @out = "out")
        {
            try
            {
                string resp = Web($"{host}/api/ItemRequest/{@out}/{bid}/?key={key}", timeout: 15000);
                Trade tr = JsonConvert.DeserializeObject<Trade>(resp);
                if (tr.success)
                    return tr;
                else
                {
                    //Если трейд не получен, то смотрим ошибку. 
                    //Если ошибка в обновлении инвентаря, то обновляем
                    if (tr.error?.IndexOf("загрузить", StringComparison.OrdinalIgnoreCase) != -1)
                    {
                        ConsoleInputOutput.OutputMessage("Не удалось загрузить инвентарь.\nОбновляем", MessageType.Default);
                        UpdateInvent(host, key);
                        return null;
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
		/// Запросить id бота
		/// </summary>
        public static string GetBotId(string host, string key)
        {
            try
            {
                string resp = Web($"{host}/api/Trades/?key={key}", timeout: 15000);
                var trades = JsonConvert.DeserializeObject<List<TradeOffer>>(resp);
                foreach (var t in trades)
                {
                    if (t.ui_bid != "0" && t.ui_status == "4")
                    {
                        return t.ui_bid;
                    }

                }
                return "0";
            }
            catch
            {
                return "0";
            }
        }

        /// <summary>
		/// Отослать уведомление сайту об онлайне
		/// </summary>
		/// <param name="key"></param>
        public static void Ping(string host, string key)
        {
            Web($"{host}/api/PingPong/?key={key}");
        }

        /// <summary>
        /// Получение ключа для сообщений сокетов
        /// </summary>
        /// <param name="key">api key</param>
        /// <returns>string socket key</returns>
        public static string GetWS(string host, string key)
        {
            return Web($"{host}/api/GetWSAuth/?key={key}");
        }
        //

        //Веб запрос
        private static string Web(string server, int timeout = 1000, string list = "")
        {
            try
            {
                HttpWebRequest myHttpWebRequest = (HttpWebRequest)HttpWebRequest.Create(server);
                myHttpWebRequest.Timeout = timeout;
                myHttpWebRequest.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/535.2 (KHTML, like Gecko) Chrome/15.0.874.121 Safari/535.2";
                if (list != "")
                {
                    myHttpWebRequest.Method = "POST";
                    var data = Encoding.UTF8.GetBytes("list=" + list);
                    myHttpWebRequest.ContentLength = data.Length;
                    myHttpWebRequest.ContentType = "application/x-www-form-urlencoded";

                    using (var stream = myHttpWebRequest.GetRequestStream())
                        stream.Write(data, 0, data.Length);
                }
                using (var myHttpWebResponse = myHttpWebRequest.GetResponse())
                {
                    using (StreamReader myStreamReader = new StreamReader(myHttpWebResponse.GetResponseStream()))
                    {
                        return myStreamReader.ReadToEnd();
                    }
                }
            }
            catch (WebException e)
            {
                switch (e.Status)
                {
                    case WebExceptionStatus.ProtocolError:
                    case WebExceptionStatus.Timeout:
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(e.Message);
                        Console.ResetColor();
                        break;
                }
                return "{\"success\":\"False\"}";
            }
        }

        private static async Task<string> _WebAsync(string server, int timeout = 1000, string list = "")
        {
            try
            {
                HttpWebRequest myHttpWebRequest = (HttpWebRequest)HttpWebRequest.Create(server);
                myHttpWebRequest.Timeout = timeout;
                myHttpWebRequest.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/535.2 (KHTML, like Gecko) Chrome/15.0.874.121 Safari/535.2";
                if (list != "")
                {
                    myHttpWebRequest.Method = "POST";
                    var data = Encoding.UTF8.GetBytes("list=" + list);
                    myHttpWebRequest.ContentLength = data.Length;
                    myHttpWebRequest.ContentType = "application/x-www-form-urlencoded";

                    using (var stream = myHttpWebRequest.GetRequestStream())
                        stream.Write(data, 0, data.Length);
                }
                using (var myHttpWebResponse = await myHttpWebRequest.GetResponseAsync())
                {
                    using (StreamReader myStreamReader = new StreamReader(myHttpWebResponse.GetResponseStream()))
                    {
                        return await myStreamReader.ReadToEndAsync();
                    }
                }
            }
            catch (WebException e)
            {
                switch (e.Status)
                {
                    case WebExceptionStatus.ProtocolError:
                    case WebExceptionStatus.Timeout:
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(e.Message);
                        Console.ResetColor();
                        break;
                }
                return "{\"success\":\"False\"}";
            }
        }


        /// <summary>
        /// Получения списка быстрых покупок
        /// </summary>
        public static List<QItems> QList(string host, string key)
        {
            try
            {
                string answer = Web($"{host}/api/QuickItems/?key={key}");
                var qData = JsonConvert.DeserializeObject<QData>(answer);
                if (qData != null && qData.success)
                {
                    return qData.items;
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Купить предмет из списка быстрых покупок
        /// </summary>
        /// <param name="id">Id предмета</param>
        /// <param name="host"></param>
        /// <param name="key">Ключ</param>
        /// <returns>True - покупка удалась; False - покупку не удалось совершить</returns>
        public static bool QBuy(string id, string host, string key)
        {
            try
            {
                string answer = Web($"{host}/api/QuickBuy/{id}/?key={key}");
                var b = JsonConvert.DeserializeObject<QAnswer>(answer);
                return b.success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Список отправленных маркетов трейдов
        /// </summary>
        public static MarketTrades MarketTrades(string host, string key)
        {
            try
            {
                string answer = Web($"{host}/api/MarketTrades/?key={key}");
                var b = JsonConvert.DeserializeObject<MarketTrades>(answer);
                return b.success ? b : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Обновить инвентарь
        /// </summary>
        public static void UpdateInvent(string host, string key)
        {
            Web($"{host}/api/UpdateInventory/?key={key}");
            /* string answer = Web($"{host}/api/UpdateInventory/?key={key}");

			 var status = new { success = false };
			 var inf = JsonConvert.DeserializeAnonymousType(answer, status);*/
        }

        /// <summary>
        /// Удалить все ордеры на быструю покупку
        /// </summary>
        /// <param name="key">api key</param>
        /// <returns></returns>
        public static bool DeleteAllOrders(string host, string key)
        {
            try
            {
                string answer = Web($"{host}/api/DeleteOrders/?key={key}");
#if DEBUG
                System.Diagnostics.Debug.WriteLine(answer);
#endif
                var pr = new { success = false, deleted_orders = 0, error = "" };
                var inf = JsonConvert.DeserializeAnonymousType(answer, pr);
                return inf.success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Добавление ордера на покупку
        /// </summary>
        public static string ProcessOrder(Itm item, string host, string key)
        {
            try
            {
                //https://market.csgo.com/api/ProcessOrder/[classid]/[instanceid]/[price]/?key=[your_secret_key]
                string answer = Web($"{host}/api/ProcessOrder/{item.id.Replace('_', '/')}/{item.price * 100}/?key={key}");
                var pr = new { success = false, way = "", error = "" };
                var inf = JsonConvert.DeserializeAnonymousType(answer, pr);
                if (inf.success)
                {
                    return inf.way;
                }
                else
                {
                    return inf.error;
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// Подписка на уведомления в сокетах на выбранные предметы
        /// </summary>
        /// <param name="item">Предмет</param>
        /// <param name="key">api ключ</param>
        /// <param name="delNotif">Удалить уведомление</param>
        public static ReturnResult<bool> Notification(Itm item, string host, string key, bool delNotif)
        {
            var result = new ReturnResult<bool>();
            string answerStr = "";
            try
            {
                //https://market.csgo.com/api/UpdateNotification/[classid]/[instanceid]/[price]?key=[your_secret_key]
                if (delNotif)
                {
                    answerStr = Web($"{host}/api/UpdateNotification/{item.id.Replace('_', '/')}/0/?key={key}");
                }
                else
                {
                    answerStr = Web($"{host}/api/UpdateNotification/{item.id.Replace('_', '/')}/{item.price * 100}/?key={key}");
                }

                var pr = new { success = false, error = "" };
                var inf = JsonConvert.DeserializeAnonymousType(answerStr, pr);

                result.success = inf.success;

                if (!inf.success)
                {
                    result.errorMessage = result.errorMessage = $"{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{inf.error}\nData:{answerStr}\n{_line}";
                }
            }
            catch (Exception ex)
            {
                result.success = false;
                result.errorMessage = $"{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{ex.Message}\nData:{answerStr}\nTarget:{ex.Data}\n{_line}";
            }
            return result;
        }

        /// <summary>
        /// Запросить скидку
        /// </summary>
        /// <param name="key"></param>
        /// <returns>Если не было ошибок - скидку; если были, то значения: 
		/// 10.01 - Bad key
		/// 10.02 - Exception
		/// 10.03 - Иная ошибка</returns>
        public static double GetDiscounts(string host, string key)
        {
            try
            {
                //https://market.csgo.com/api/GetDiscounts/?key=[your_secret_key]
                string answer = Web($"{host}/api/GetDiscounts/?key={key}");

                var pr = new { success = false, discounts = new { sell_fee = "" }, error = "" };
                var inf = JsonConvert.DeserializeAnonymousType(answer, pr);
                if (inf.success)
                {
                    return Convert.ToDouble(inf.discounts.sell_fee.Replace("%", ""), System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    if (inf.error.ToLower() == "bad key")
                    {
                        return 10.01;
                    }
                    else
                    {
                        return 10.03;
                    }
                }
            }
            catch
            {
                return 10.02;
            }
        }

        /// <summary>
        /// Получение дневной прибыли
        /// </summary>
        /// <returns>Примерная дневная прибыль в копейках</returns>
        public static int GetProfit(string host, string key)
        {
            try
            {
                int spent = 0, earned = 0;
                var startDay = new DateTimeOffset(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0, 0, 1, TimeSpan.Zero).ToUnixTimeSeconds();
                var endDay = new DateTimeOffset(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 23, 59, 59, TimeSpan.Zero).ToUnixTimeSeconds();
                //https://market.csgo.com/api/OperationHistory/[start_time]/[end_time]/?key=[your_secret_key]
                var answer = OperationHistory(startDay,endDay, host, key);

                if (answer.success && answer.dataResult != null)
                {
                    foreach (var i in answer.dataResult)
                    {
                        if (i.h_event == GetPlace(host,"sell"))
                        {
                            earned += Convert.ToInt32(i.recieved);
                        }
                        if (i.h_event == GetPlace(host, "buy") && i.stage != "5")
                        {
                            spent += Convert.ToInt32(i.recieved);
                        }
                    }
                    return earned - spent;
                }
                else
                {
                    return -1;
                }

            }
            catch
            {
                return -2;
            }
        }

        /// <summary>
        /// Уйти офлайн
        /// </summary>
        public static void GoOffline(string host, string key)
        {
            // https://market.csgo.com/api/GoOffline/?key=
            Web($"{host}/api/GoOffline/?key={key}");
        }

        /// <summary>
        /// Проверяет сколько предметов на передачу и прием висит в профиле
        /// </summary>
        /// <param name="key">Api ключ</param>
        /// <returns>Возвращает переменную с двумя значениями: getCount - сколько нужно принять; outCount - сколько нужно передать</returns>
        public static InventOffersInfo CountItemsToTransfer(string host, string key)
        {
            try
            {
                var returnVariable = new InventOffersInfo();

                string resp = Web($"{host}/api/Trades/?key={key}");
                resp = resp.Insert(0, "{\"items\":");
                resp += "}";
                var items = JsonConvert.DeserializeObject<Trades>(resp);
                foreach (var item in items.items)
                {
                    switch (item.ui_status)
                    {
                        case "4":
                            returnVariable.outCount++;
                            break;
                        case "2":
                            returnVariable.inCount++;
                            break;
                    }
                }
                return returnVariable;
            }

            catch
            {
                return null;
            }
        }
        /// <summary>
        /// Получить список включенных уведомлений о изменении цены
        /// </summary>
        public static ReturnResult<List<NotificationsItems>> GetNotifications(string host, string key)
        {
            //https://market.csgo.com/api/GetNotifications/?key=[your_secret_key]
            var result = new ReturnResult<List<NotificationsItems>>();
            string answerStr = Web($"{host}/api/GetNotifications/?key={key}");
            try
            {
                var desirAns = JsonConvert.DeserializeObject<CNotifications>(answerStr);

                result.success = desirAns.success;

                if (desirAns.success)
                {
                    result.dataResult = desirAns.Notifications;
                }
                else
                {
                    result.errorMessage = $"{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{desirAns.error}\nData:{answerStr}\n{_line}";
                }
            }
            catch (Exception ex)
            {
                result.success = false;
                result.errorMessage = $"{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{ex.Message}\nData:{answerStr}\nTarget:{ex.Data}\n{_line}";
            }
            return result;
        }

        /// <summary>
        /// Вся информация о предметах в одном месте через POST запрос.
        /// </summary>
        /// <param name="data">list — classid_instanceid,classid_instanceid,classid_instanceid,classid_instanceid,...</param>
        /// <param name="sell">
        /// 0 - Не получать предложения о продаже
        /// 1 - Получать топ 50 дешевых предложений + свой
        /// 2 - Получать только 1 самый дешевый оффер о продаже</param>
        /// <param name="buy">
        /// 0 - Не получать запросы на покупку (ордера)
        /// 1 - Получать топ 50 самых высоких запросов на покупку(ордеров) + свой
        /// 2 - Получать только 1 самый высокий запрос на покупку</param>
        /// <param name="history">
        /// 0 - Не получать история торгов по предмету
        /// 1 - Получить информацию о последних 100 продажах
        /// 2 - Получить информацию о последних 10 продажах</param>
        /// <param name="info">
        /// 0 - Не получать информацию о предмете
        /// 1 - Получить базовую информацию(название, тип)
        /// 2 - Получить дополнительно хэш для покупки, ссылку на картинку
        /// 3 - Получать дополнительно описание предмета и теги из Steam</param>
        /// <returns>MassInfo</returns>
        public static ReturnResult<List<MassInfoResult>> MassInfo(string host, string key, string data, uint sell = 2, uint buy = 0, uint history = 0, uint info = 0)
        {
            //https://market.csgo.com/api/MassInfo/[SELL]/[BUY]/[HISTORY]/[INFO]?key=[your_secret_key]
            var result = new ReturnResult<List<MassInfoResult>>();
            var answerStr = Web($"{host}/api/MassInfo/{sell}/{buy}/{history}/{info}/?key={key}", list: data);
            try
            {
                var desirAns = JsonConvert.DeserializeObject<MassInfo>(answerStr.Replace("false","null"));

                result.success = desirAns.success;
                if (desirAns.success)
                {
                    result.dataResult = desirAns.results;
                }
                else
                {
                    result.errorMessage = $"\n{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{desirAns.error}\nData:{answerStr}\n{_line}";
                }
            }
            catch (Exception ex)
            {
                result.success = false;
                result.errorMessage = $"{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{ex.Message}\nData:{answerStr}\nTarget:{ex.Data}\n{_line}";
            }
            return result;
        }

        /// <summary>
        /// Получение инвентаря Steam, только те предметы, которые Вы еще не выставили на продажу.
        /// </summary>
        public static ReturnResult<List<GetInvDatum>> GetInv(string host, string key)
        {
            //https://market.csgo.com/api/GetInv/?key=[your_secret_key]
            var result = new ReturnResult<List<GetInvDatum>>();
            var answerStr = Web($"{host}/api/GetInv/?key={key}", 2000);
            try
            {
                var desirAns = JsonConvert.DeserializeObject<GetInv>(answerStr);

                result.success = desirAns.success;

                if (desirAns.success && desirAns.data != null)
                {
                    result.dataResult = desirAns.data;
                }
                else
                {
                    result.errorMessage = $"\n{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{desirAns.error}\nData:{answerStr}\n{_line}";
                }
            }
            catch (Exception ex)
            {
                result.success = false;
                result.errorMessage = $"{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{ex.Message}\nData:{answerStr}\nTarget:{ex.Data}\n{_line}";
            }
            return result;
        }

        /// <summary>
        /// Выставить новый предмет на продажу. Чтобы получить список предметов для выставления, воспользуйтесь методом GetInv.
        /// </summary>
        /// <param name="item">Предмет для выставления типа GetInvDatum</param>
        /// <param name="price">Цена в копейках</param>
        /// <returns>true - выставлен</returns>
        public static ReturnResult<bool> SetPrice(GetInvDatum item, int price, string host, string key)
        {
            //https://market.csgo.com/api/SetPrice/new_[classid]_[instanceid]/[price]/?key=[your_secret_key]
            var result = new ReturnResult<bool>();
            var answerStr = Web($"{host}/api/SetPrice/{item.ui_id}/{price}?key={key}", 2000);
            try
            {
                var type = new { success = false, error = "", result = 0, price = 0 };

                var desirAns = JsonConvert.DeserializeAnonymousType(answerStr, type);

                result.success = desirAns.success;

                if (!desirAns.success)
                {
                    result.success = false;
                    result.errorMessage = $"\n{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{desirAns.error}\nData:{answerStr}\n{_line}";
                }
            }
            catch (Exception ex)
            {
                result.success = false;
                result.errorMessage = $"{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{ex.Message}\nData:{answerStr}\nTarget:{ex.Data}\n{_line}";
            }
            return result;
        }

        /// <summary>
        /// Получить предложения о продаже определенного предмета.
        /// </summary>
        public static ReturnResult<List<Offer>> SellOffers(string classid, string instanceid, string host, string key)
        {
            //https://market.csgo.com/api/SellOffers/[classid]_[instanceid]/?key=[your_secret_key]
            var result = new ReturnResult<List<Offer>>();
            var answerStr = Web($"{host}/api/SellOffers/{classid}_{instanceid}?key={key}", 2000);
            try
            {
                var type = new { success = false, error = "", offers = new List<Offer>(), best_offer = "" };
                var desirAns = JsonConvert.DeserializeAnonymousType(answerStr, type);

                result.success = desirAns.success;
                if (desirAns.success && desirAns.offers != null)
                {

                    result.dataResult = desirAns.offers;
                }
                else
                {
                    result.errorMessage = $"{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{desirAns.error}\nData:{answerStr}\n{_line}";
                }
            }
            catch (Exception ex)
            {
                result.success = false;
                result.errorMessage = $"{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{ex.Message}\nData:{answerStr}\nTarget:{ex.Data}\n{_line}";
            }
            return result;
        }

        /// <summary>
        /// Получить историю операций на всех маркетах за определенный период времени.
        /// </summary>
        /// <param name="startTime">unix time в секундах начала периода</param>
        /// <param name="endTime">unix time в секундах конца периода</param>
        public static ReturnResult<List<OperationHistory>> OperationHistory(long startTime, long endTime, string host, string key)
        {
            //https://market.csgo.com/api/OperationHistory/[start_time]/[end_time]/?key=[your_secret_key]
            var answerStr = Web($"{host}/api/OperationHistory/{startTime}/{endTime}/?key={key}");
            var result = new ReturnResult<List<OperationHistory>>();
            try
            {
                var pr = new { success = false, history = new List<OperationHistory>(), error = "" };
                var desirAns = JsonConvert.DeserializeAnonymousType(answerStr, pr);

                result.success = desirAns.success;

                if (desirAns.success && desirAns.history != null)
                {
                    result.dataResult = desirAns.history;
                }
                else
                {
                    result.errorMessage = $"{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{desirAns.error}\nData:{answerStr}\n{_line}";
                }
            }
            catch (Exception ex)
            {
                result.success = false;
                result.errorMessage = $"{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{ex.Message}\nData:{answerStr}\nTarget:{ex.Data}\n{_line}";
                
            }
            return result;
        }


        public static ReturnResult<InventoryItems> InventoryItems(string host, string key)
        {
            //https://market.csgo.com/api/InventoryItems/?key=[your_secret_key]
            var answerStr = Web($"{host}/api/InventoryItems/?key={key}");
            var result = new ReturnResult<InventoryItems>();
            try
            {
                var desirAns = JsonConvert.DeserializeObject<InventoryItems>(answerStr);

                result.success = desirAns.Success;

                if (desirAns.Success)
                {
                    result.dataResult = desirAns;
                }
                else
                {
                    result.errorMessage = desirAns.Error;
                }
                    
            }
            catch (Exception ex)
            {
                result.success = false;
                result.errorMessage =
                    $"{_line}\nMethod:{MethodBase.GetCurrentMethod().Name}\nMessage:{ex.Message}\nData:{answerStr}\nTarget:{ex.Data}\n{_line}";
            }
            return result;
        }

        public static string GetPlace(string host, string type)
        {
            var place = new StringBuilder();
            place.Append(type);
            place.Append("_");
            switch (host)
            {
                case Host.CSGO:
                    place.Append("go");
                    break;
                case Host.DOTA2:
                    place.Append("dota");
                    break;
                case Host.PUBG:
                    place.Append("pb");
                    break;
                default:
                    ConsoleInputOutput.OutputMessage("Площадка не найдена", MessageType.Error);
                    return null;
            }
            return place.ToString();
        }
    }
}
