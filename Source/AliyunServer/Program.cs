using System;
using System.Configuration;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Aliyun.Acs.Alidns.Model.V20150109;
using Aliyun.Acs.Core;
using Aliyun.Acs.Core.Profile;

namespace AliyunServer
{
    class Program
    {
        public static bool stop = false;
        public static ManualResetEvent resetEvent = new ManualResetEvent(false);

        static void Main(string[] args)
        {
            Thread t = new Thread(DomainRecordCheckThread);
            t.Start();

            Console.WriteLine("按任意键退出程序...");
            Console.ReadKey();

            stop = true;

            resetEvent.WaitOne();
        }

        public static void DomainRecordCheckThread()
        {
            string strAccessKeyId = ConfigurationManager.AppSettings["AccessKeyId"] ?? string.Empty;
            string strAccessSecret = ConfigurationManager.AppSettings["AccessKeySecret"] ?? string.Empty;

            if (string.IsNullOrEmpty(strAccessKeyId) || string.IsNullOrEmpty(strAccessSecret))
            {
                Console.WriteLine("错误： 获取不到AccessKey的参数配置");
                return;
            }

            IClientProfile clientProfile = DefaultProfile.GetProfile("cn-hangzhou", strAccessKeyId, strAccessSecret);
            DefaultAcsClient client = new DefaultAcsClient(clientProfile);

            while (!stop)
            {
                string strWanIp = GetWANIP();
                if (!string.IsNullOrEmpty(strWanIp))
                {
                    DomainRecordCheck(client, "mychen.com.cn", strWanIp);
                }

                // 10分钟检测一次
                Thread.Sleep(1000 * 20);
            }

            resetEvent.Set();
        }

        public static void DomainRecordCheck(DefaultAcsClient client, string checkDomain, string currentIP)
        {
            try
            {
                DescribeDomainRecordsRequest req = new DescribeDomainRecordsRequest();
                req.PageSize = 10;
                req.PageNumber = 1;
                req.DomainName = checkDomain;
                var rsp = client.GetAcsResponse<DescribeDomainRecordsResponse>(req);

                // 修改
                foreach (var record in rsp.DomainRecords)
                {
                    if (record.Value == currentIP)
                    {
                        continue;
                    }

                    Console.WriteLine($"域名: {checkDomain} 当前解析地址为 {record.Value}, 需要重新解析成 {currentIP}, RR: {record.RR}, Type: {record.Type}");

                    UpdateDomainRecordRequest updateReq = new UpdateDomainRecordRequest();
                    updateReq.RecordId = record.RecordId;
                    updateReq.RR = record.RR;
                    updateReq.Type = record.Type;
                    updateReq.Value = currentIP;

                    try
                    {
                        var updateRsp = client.GetAcsResponse<UpdateDomainRecordResponse>(updateReq);
                        if (updateRsp.HttpResponse.isSuccess())
                        {
                            Console.WriteLine("解析地址修改成功!");
                        }
                        else
                        {
                            Console.WriteLine("解析地址修改失败! -- " + Encoding.UTF8.GetString(updateRsp.HttpResponse.Content));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("请求修改解析地址出现异常: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("DomainRecordCheck出现异常: ", ex.Message);
            }
        }

        // 返回公网IP地址
        public static string GetWANIP()
        {
            try
            {
                WebClient client = new WebClient();
                var rspData = client.DownloadData("http://myip.ipip.net/");

                var strIPData = Encoding.UTF8.GetString(rspData);

                var reg = new Regex(@"((2[0-4]\d|25[0-5]|[01]?\d\d?)\.){3}(2[0-4]\d|25[0-5]|[01]?\d\d?)");
                var match =  reg.Match(strIPData);
                if (match != null && match.Success)
                {
                    return match.Value.Trim();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("获取WAN IP出现异常: " + ex.Message);
            }

            return string.Empty;
        }
    }
}
