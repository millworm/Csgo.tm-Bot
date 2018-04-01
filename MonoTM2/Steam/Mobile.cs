using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MonoTM2.Classes;
using Newtonsoft.Json;
using SteamAuth;

namespace MonoTM2.Steam
{
    //ToDo Исправить тут все
    internal class Mobile
    {
        private static void SteamAuthLogin()
        {
            var cfg = Config.GetConfig();
            var authLogin = new UserLogin(cfg.SteamSettings.Login, cfg.SteamSettings.Password);
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

        private static bool SaveMobileAuth(AuthenticatorLinker linker)
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

        public static void CreateAuth()
        {
            while (!File.Exists("account.maFile"))
                SteamAuthLogin();
            var sgAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText("account.maFile"));
        }

        internal static void GenerateGuardCode()
        {
            if (File.Exists("account.maFile"))
            {
                var sgAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText("account.maFile"));
                Console.WriteLine(sgAccount.GenerateSteamGuardCode());
            }
            else
            {
                Console.WriteLine("File not exist!");
            }
        }
    }
}
