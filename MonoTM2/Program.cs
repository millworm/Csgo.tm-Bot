using MonoTM2.Classes;
using MonoTM2.InputOutput;
using System;
using System.Reflection;
using System.Text;
namespace MonoTM2
{
    static class Program
    {
        static void Main()
        {
            Console.Title = $"CsgoTm bot v.{Assembly.GetEntryAssembly().GetName().Version}";
            Config.GetConfig();
            CBot bot = new CBot();
            //загрузка всех настроек
            bot.Loader();
            //стартуем
#if !DEBUG
            bot.Start();
#endif
            ConsoleInputOutput.OutputMessage("Запуск выполнен");
            string Mess = "";
            while (Mess != "q")
            {
                Mess = Console.ReadLine();
                try
                {
                    switch (Mess.ToLowerInvariant())
                    {
                        case "s":
                            bot.SetItems(Const.Host.PUBG, null);
                            bot.SetItems(Const.Host.DOTA2, null);
                            bot.SetItems(Const.Host.CSGO, null);
                            break;
                        case "c":
                            Console.Clear();
                            break;
                        /*  case "accept":
                               bot.AcceptMobileOrdersFunc();
                               break;
                           case "acceptmobile":
                               bot.AcceptMobileOrders();
                               break;*/
                        //привязка мобильного приложения 
                        case "mobile":
                            bot.CreateAuth();
                            break;
                        case "guard":
                            bot.GenerateGuardCode();
                            break;
                        //показывать какие предметы были выставлены из списка покупок
                        case "p":
                        case "profit":
                            ConsoleInputOutput.OutputMessage(bot.GetProfit());
                            break;
                        //подтвердить трейды
                        case "trade":
                        case "t":
                            if (bot.GetConfirmTradesValue())
                            {
                                bot.ConfirmTrade();
                                ConsoleInputOutput.OutputMessage("Выключить прием?");
                                var a = Console.ReadLine();

                                if (a.ToLowerInvariant() == "y")
                                {
                                    bot.TurnOnOrOffAutoConfirmTrades();

                                    if (bot.GetConfirmTradesValue())
                                    {
                                        ConsoleInputOutput.OutputMessage("Отключено");
                                    }
                                }
                            }
                            else
                            {
                                bot.TurnOnOrOffAutoConfirmTrades();
                            }
                            break;
                        //добавить предмет
                        case "add":
                        case "a":
                            ConsoleInputOutput.OutputMessage("Введите ссылку");
                            var link = Console.ReadLine();
                            bot.AddItem(link);
                            break;
                        //обноление
                        case "update":
                            //обноление цен
                            bot.UpdatePrice();
                            //проверка быстрых покупок
                            bot.CheckQuickOrders();
                            //обновление ордеров и оповщений
                            bot.UpdateOrdersAndNotifications();

                            break;
                        //вывести прибыль с предмета
                        case "gprice":
                            var hostGprice = GetPlace();

                            ConsoleInputOutput.OutputMessage(bot.GetPrice(hostGprice).ToString());
                            break;
                        case "sprice":
                            var hostSprice = GetPlace();

                            ConsoleInputOutput.OutputMessage("Введите прибыль");
                            bot.SetPrice(Console.ReadLine(), hostSprice);
                            ConsoleInputOutput.OutputMessage("Новая прибыль " + bot.GetPrice(hostSprice));

                            break;
                        //вывести список предметов	
                        case "list":
                            Console.ForegroundColor = ConsoleColor.Magenta;
                            int n = 0;
                            var tempItems = bot.GetListItems();

                            var sb = new StringBuilder();
                            sb.Append(Environment.NewLine);
                            sb.AppendLine($" №          {$"{"Название",-40}"}\tЦена\tПрибыль\tТип");

                            foreach (var host in tempItems)
                            {
                                var place = "";
                                switch (host.Key)
                                {
                                    case Const.Host.CSGO:
                                        place = "Csgo";
                                        break;
                                    case Const.Host.DOTA2:
                                        place = "Dota2";
                                        break;
                                    case Const.Host.PUBG:
                                        place = "Pubg";
                                        break;
                                }
                                foreach (var item in host.Value)
                                {
                                    var profitList = bot.GetPrice(host.Key);
                                    var nameList = $"{item.name.Substring(0, item.name.Length>40 ? 40 : item.name.Length),-40}";
                                    string typeList = Convert.ToString(item.priceCheck).Substring(0, 5);

                                        sb.AppendLine(
                                            $"[{n++:00}]{$"[{place}]",-7} {nameList}\t{item.price}\t{(item.profit != 0?item.profit: profitList)}\t{typeList}");
                                }
                                if(host.Value.Count > 0)
                                    sb.AppendLine(new string('-', 50));
                                n = 0;
                            }
                            ConsoleInputOutput.OutputMessage(sb.ToString(), MessageType.GiveWeapon);//для окраски в розовый в консоли
                            Console.ResetColor();

                            break;

                        //удалить предмет
                          case "delete":
                          case "d":
                              var hostDelete = GetPlace();
                            ConsoleInputOutput.OutputMessage("Введите номер");
                              var N = Convert.ToInt16(Console.ReadLine());
                              ConsoleInputOutput.OutputMessage($"Удалить {bot.GetItem(N, hostDelete).name}  ?");
                              var A = Console.ReadLine().ToLowerInvariant();
                              if (A == "y")
                              {
                                  bot.RemoveItem(N, hostDelete);
                                  ConsoleInputOutput.OutputMessage("Удалено");
                              }
                              break;
                        case "stime":
                            var hostStime = GetPlace();
                            ConsoleInputOutput.OutputMessage("Введите время");
                            bot.SetUpdateTimer(Console.ReadLine(), hostStime);
                            ConsoleInputOutput.OutputMessage("Новое время " + bot.GetUpdateTimer(hostStime));
                            break;
                        case "gtime":
                            var hostGtime = GetPlace();
                            ConsoleInputOutput.OutputMessage("Время " + bot.GetUpdateTimer(hostGtime));
                            break;
                        case "status":
                            ConsoleInputOutput.OutputMessage(bot.Status());
                            break;
                        case "sprofit":
                            var hostSprofit = GetPlace();
                            ConsoleInputOutput.OutputMessage("Введите номер вещи и прибыль через пробел (пример: 2 20).\nДля использования общей прибыли выставить 0");
                            var line = Console.ReadLine();
                            var data = line.Split();
                            if (data.Length != 2)
                            {
                                ConsoleInputOutput.OutputMessage("Неверный формат");
                            }
                            else
                            {
                                var id = Convert.ToInt32(data[0]);
                                var profit = Convert.ToInt32(data[1]);

                                if (id >= bot.GetListItems().Count || id < 0)
                                {
                                    ConsoleInputOutput.OutputMessage("Номер за пределами списка вещей");
                                    break;
                                }
                                if (profit < 0)
                                {
                                    ConsoleInputOutput.OutputMessage("Прибыль ниже нуля");
                                    break;
                                }

                                var item = bot.GetItem(id, hostSprofit);
                                ConsoleInputOutput.OutputMessage($"Выставить прибыль для {item.name} прибыль в {profit} копеек?");

                                var spC = Console.ReadLine().ToLowerInvariant();
                                if (spC == "y")
                                {
                                    if (bot.SetProfit(hostSprofit, id, profit))
                                        ConsoleInputOutput.OutputMessage("Выставлено");
                                    else
                                        ConsoleInputOutput.OutputMessage("Ошибка");
                                }
                            }
                            break;
                        case "scheck":
                            var hostScheck = GetPlace();
                            ConsoleInputOutput.OutputMessage("Сменить способ проверки цен для предмета.\nВведите номер предмета и тип проверки (0 - обычная, 1 - уведомления)\nПример: 1 1");
                            line = Console.ReadLine();
                            data = line.Split();
                            if (data.Length != 2)
                            {
                                ConsoleInputOutput.OutputMessage("Неверный формат");
                                break;
                            }

                            var _id = Convert.ToInt32(data[0]);
                            var type = Convert.ToInt32(data[1]);
                            if (_id >= bot.GetListItems().Count || _id < 0)
                            {
                                ConsoleInputOutput.OutputMessage("Номер за пределами списка вещей");
                                break;
                            }
                            if (type < 0 || type > 1)
                            {
                                ConsoleInputOutput.OutputMessage("Неизвестный тип");
                                break;
                            }

                            bot.SetPriceCheck(hostScheck, _id, type);
                            break;
                        case "gcheck":
                            var hostGcheck = GetPlace();
                            ConsoleInputOutput.OutputMessage("Введите номер предмета");
                            var gcheckId = Convert.ToInt32(Console.ReadLine());
                            if (gcheckId >= bot.GetListItems().Count || gcheckId < 0)
                            {
                                ConsoleInputOutput.OutputMessage("Номер за пределами списка вещей");
                            }
                            else
                            {
                                var typeCheck = bot.GetPriceCheck(hostGcheck, gcheckId);
                                switch (typeCheck)
                                {
                                    case PriceCheck.Notification:
                                        ConsoleInputOutput.OutputMessage("Уведомления");
                                        break;
                                    case PriceCheck.Price:
                                        ConsoleInputOutput.OutputMessage("Обычный");
                                        break;
                                }
                            }
                            break;
                        default:
                            ConsoleInputOutput.OutputMessage("Команда неизвестна");
                            break;
                    }
                }
                catch (Exception tty)
                {
                    ConsoleInputOutput.OutputMessage(tty.Message);
                }
            }
            bot.Dispose();
        }

        static string GetPlace()
        {
            ConsoleInputOutput.OutputMessage("Для какой площадки устанавливается прибыль?\nCSGO; Dota2; PUBG");
            var placeSprice = Console.ReadLine().ToLowerInvariant();
            var hostSprice = Const.Host.FindHost(placeSprice);

            if (hostSprice == null)
                ConsoleInputOutput.OutputMessage("Площадка не найдена. Попробуйте еще раз");
            else
                return hostSprice;

            throw new NullReferenceException();
        }
    }

}
