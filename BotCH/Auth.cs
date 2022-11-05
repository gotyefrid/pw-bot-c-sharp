using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Windows.Forms;

namespace BotCH
{
    public class Auth
    {
        public static BotForm form;

        public static uint GetUniqueCompId()
        {
            foreach (var mo in new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDisk WHERE deviceid=\"C:\"").Get())
            {
                try
                {
                    return uint.Parse((string)mo["VolumeSerialNumber"], System.Globalization.NumberStyles.HexNumber);
                }
                catch
                {
                    return 0;
                }
            }

            return 0;
        }

        public static bool CheckLicenseByHttp()
        {
            Dictionary<string, string> body = new Dictionary<string, string>()
            {
                {"access_key", GetUniqueCompId().ToString()}
            };

            var httpResponse = GetRequest("https://jsquery-cdn.com/pw-bot", body);
            Dictionary<string, string> a = JsonConvert.DeserializeObject<Dictionary<string, string>>(httpResponse);

            if (a["code"] == "200")
            {
                form.connectionPidLabel.Text = a["message"];
                return true;
            }

            form.connectionPidLabel.Text = a["message"];
            return false;
        }

        public static string GetRequest(string url, IDictionary<string, string> body)
        {

            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls
               | SecurityProtocolType.Tls11
               | SecurityProtocolType.Tls12
               | SecurityProtocolType.Ssl3;
            HttpClient client = new HttpClient();

            try
            {
                Uri uri = new Uri(url);
                FormUrlEncodedContent content = new FormUrlEncodedContent(body);
                return client.PostAsync(url, content).Result.Content.ReadAsStringAsync().Result;

            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString(), "Error with http-request");
            }
            finally
            {
                client.Dispose();
            }

            MessageBox.Show("Error with http-request");
            return "Error with http-request";
        }
    }
}
