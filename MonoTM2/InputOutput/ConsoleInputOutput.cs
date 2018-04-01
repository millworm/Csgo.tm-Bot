using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MonoTM2.InputOutput
{
    public class ConsoleInputOutput: IInputOutput
    {
        private static readonly object writeLocker = new object();

        public static async void OutputMessage(string msg, MessageType msgType = MessageType.Default)
        {
            var commandBuilder = new List<Action>();
            ConsoleColor color = ConsoleColor.Gray;
            switch (msgType)
            {
                case MessageType.BuyWeapon:
                    color = ConsoleColor.Green;
                    break;

                case MessageType.Error:
                    color = ConsoleColor.Red;
                    break;

                case MessageType.GetWeapon:
                    color = ConsoleColor.Cyan;
                    break;

                case MessageType.GiveWeapon:
                    color = ConsoleColor.Magenta;
                    break;

                case MessageType.Info:
                    color = ConsoleColor.Gray;
                    break;

                case MessageType.Socket:
                    color = ConsoleColor.Gray;
                    break;

                case MessageType.Timer:
                    color = ConsoleColor.Blue;
                    break;
                case MessageType.Default:
                    break;
            }

            commandBuilder.Add(Console.ResetColor);
            commandBuilder.Add(() => Console.Write($"[{DateTime.Now.ToString("HH:mm:ss")}] "));
            commandBuilder.Add(() => Console.ForegroundColor = color);
            commandBuilder.Add(() => Console.WriteLine(msg));
            commandBuilder.Add(Console.ResetColor);

           await Print(commandBuilder);
        }

        private static Task Print(List<Action> actions)
        {
            return Task.Run(() =>
            {
                lock (writeLocker)
                    actions.ForEach(cmd => cmd());
            });
        }

        void IInputOutput.OutputMessage(string msg, MessageType msgType)
        {
            OutputMessage(msg, msgType);
        }
    }
}
