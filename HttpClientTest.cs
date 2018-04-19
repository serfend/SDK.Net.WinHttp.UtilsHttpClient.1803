using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNet4.Utilities.UtilHttp;

namespace DotNet4.Utilities.UtilHttp
{
	class HttpClientTest
	{
		private HttpClient http;
		public void NormalHttp()
		{
			var task = new Thread(()=> {
				Console.WriteLine("proxyBegin");
				Thread.Sleep(5000);
				http = new HttpClient();
				http.GetHtml("http://www.baidu.com", callBack: (x) =>
				{
					Console.WriteLine(x.response.DataString(Encoding.UTF8).Length.ToString());
					Thread.Sleep(5000);
				});
			});
			task.Start();
		}
		
	}
}
