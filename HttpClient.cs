using DotNet4.Utilities.UtilCode;
using DotNet4.Utilities.UtilHttp.HttpApiEvent;
using DotNet4.Utilities.UtilHttp.HttpException;
using DotNet4.Utilities.UtilReg;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using WinHttp;

namespace DotNet4.Utilities.UtilHttp
{
	public class HttpApi: WinHttp.WinHttpRequestClass
	{
		public Action<HttpApiEvent.ErrorEventDelegate> OnHttpError;
		public Action<HttpApiEvent.DocumentReady> OnDocumentReady;
		[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
		public HttpApi()
		{
			OnError += HttpApi_OnError;
			OnResponseFinished += HttpApi_OnResponseFinished;
		}
		[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
		public HttpApi(bool UsedFidder) : this()
		{
			if (UsedFidder) this.SetProxy(2, "127.0.0.1:8888");
		}
		private void HttpApi_OnError(int ErrorNumber, string ErrorDescription)
		{
			Logger.SysLog(this.ToString()+"\n"+ ErrorNumber+":"+ ErrorDescription + "\n" );
			OnHttpError?.BeginInvoke(new HttpApiEvent.ErrorEventDelegate(ErrorNumber, ErrorDescription),(obj)=> { },null);
		}
		public override string ToString()
		{
			var cstr = new StringBuilder();
			cstr.Append("HttpClientApi:").Append("\n");
			for (int i = 0; i < 20; i++)
			{
				if (i == 5||i==9) continue;
				cstr.Append(((WinHttpRequestOption)i).ToString());
				cstr.Append(':').Append(this.Option[(WinHttpRequestOption)i]).Append('\n');
			}
			return cstr.ToString();
		}
		/// <summary>
		/// 取出所有Cookies
		/// </summary>
		/// <returns></returns>
		public static Dictionary<string,string> GetAllCookies(string temp)
		{
			var dic = new Dictionary<string, string>();
			var cookies = temp.Split(new string[] { "Set-Cookie:" }, StringSplitOptions.RemoveEmptyEntries);
			for(int i = 1; i < cookies.Length; i++)
			{
				var item = cookies[i].Split('=');
				var key = item[0].Trim(' ');
				var value = item[1].Substring(0, item[1].IndexOf(';'));
				dic[key]=value;
			}
			return dic;
		}

		private void HttpApi_OnResponseFinished()
		{
			OnDocumentReady?.BeginInvoke(new HttpApiEvent.DocumentReady(this.ResponseBody), (obj) => { }, null);
		}



	}
	namespace HttpApiEvent
	{
		public class HttpDocument : DocumentReady
		{
			public HttpContentItem response;

			public HttpDocument(ref DocumentReady document,ref HttpApi http, HttpContentItem item) :base(document.Data)
			{
				SetResponse(item, http.GetAllResponseHeaders());
				item.Data = this.Data;
			}

			public HttpDocument(object responseBody, string headers, HttpContentItem item) : base(responseBody)
			{
				SetResponse(item, headers);
				item.Data = this.Data;
			}
			private void SetResponse(HttpContentItem item,string headers)
			{
				response = item;
				response.CookiesDic = response.CookiesDic.Union(HttpApi.GetAllCookies(headers)).ToDictionary(items=>items.Key,items=>items.Value);
				response.Headers = headers;
			}
		}
		public class ErrorEventDelegate
		{
			private int errNumber;
			private string errDescription;

			public ErrorEventDelegate(int errNumber, string errDescription)
			{
				this.ErrNumber = errNumber;
				this.ErrDescription = errDescription;
			}

			public int ErrNumber { get => errNumber; set => errNumber = value; }
			public string ErrDescription { get => errDescription; set => errDescription = value; }
		}
		public class DocumentReady
		{
			public byte[] Data;
			public DocumentReady(ref byte[] data)
			{
				this.Data = data;
			}

			public DocumentReady(object responseBody)
			{
				this.Data = (byte[])responseBody;
			}
		}
	}

	class HttpClient
	{
		public static bool UsedFidder { get;  set; }
		public static bool AlwayClearItem { get; set; }
		private HttpItem item;
		private CookieContainer cookie;
		private bool alwaysClearItem;
		private HttpItem initItem;
		public void ClearItem(HttpItem newItem=null) {
			item = newItem ?? new HttpItem();
		}
		public void ClearCookie(CookieContainer newCookies=null) { cookie = newCookies?? new CookieContainer(); }
		internal HttpItem Item { get => item; set => item = value; }
		public bool AlwaysClearItem { get => alwaysClearItem; set => alwaysClearItem = value; }
		public bool UseFidder { get; set; }
		public HttpItem InitItem { get => initItem; set => initItem = value; }
		

		public HttpClient()
		{
			ClearItem();
			ClearCookie();
			AlwaysClearItem = HttpClient.AlwayClearItem;
			UseFidder = HttpClient.UsedFidder;

		}
		
		public HttpClientChild GetHtml(string url=null,string method=null,string postData=null,string userAgent=null,string host=null,string referer =null ,bool ifModifies=true,int timeOut=30000,string clientId =null,Action<HttpApiEvent.HttpDocument>callBack=null)
		{
			if (alwaysClearItem) ClearItem(InitItem);
			if(host!=null)item.Host = host;
			if(url!=null)item.Url = url;
			if (method != null) item.Method = method;
			if(referer!=null)item.Referer = referer;
			item.IfModified = ifModifies;
			if (userAgent != null)
			{
				item.UserAgent = userAgent;
				item.UseRandomAgent = false;
			}
			else { item.UseRandomAgent = true; }
			if(postData!=null)item.Request.PostData = postData;
			if (Item.Url == null || Item.Url.Length == 0)
				return null;
			var uri = new Uri(item.Url);

			item.Request.Cookies = HttpContentItem.GetContainerCookies(cookie,"http://"+ uri.Host);
			var child = new HttpClientChild(this, HttpClient.UsedFidder)
			{
				ID = clientId
			};
			child.DocumentReady += (x, xx) =>
			{
				cookie.SetCookies(new Uri("http://"+uri.Host) , xx.response.Cookies);
				callBack?.BeginInvoke(xx,(obj)=> { },null);
			};
			child.BadResponse += (x, xx) =>
			{
				var cstr = new StringBuilder();
				cstr.Append(x).Append(":").Append(xx);
				Logger.SysLog(cstr.ToString(),"SystemLog");
			};
			do
			{
				bool sendSuccess = false;
				if (callBack != null)
					sendSuccess=child.GetResponse(item, callBack);
				else
					sendSuccess=child.GetResponse(item);
				if (sendSuccess) return child;
			} while (true);
		}

	}
	class HttpClientChild
	{
		public delegate void HttpDocumentHandler(HttpClientChild sender,HttpApiEvent.HttpDocument document);
		public event HttpDocumentHandler DocumentReady;
		public delegate void HttpErrorHandler(HttpClientChild sender,HttpApiEvent.ErrorEventDelegate error);
		public event HttpErrorHandler BadResponse;
		private HttpClient parent;
		private HttpApi http;
		public HttpDocument document = null;

		private HttpItem item;
		private long proxyBeginTime, proxyEndTime;
		private bool documentLoaded;
		private string lastUserAgent;


		/// <summary>
		/// 获取响应时间/ms
		/// </summary>
		public long ProxyTime
		{
			get=> documentLoaded?proxyEndTime - proxyBeginTime: HttpUtil.TimeStamp;
				
		}
		public string LastUserAgent { get => lastUserAgent; set => lastUserAgent = value; }
		public HttpItem Item { get => item; set => item = value; }
		public string ID { get; internal set; }


		public HttpClientChild(HttpClient parent,bool UsedFidder=false)
		{
			this.parent = parent;
			http = new HttpApi(UsedFidder);
			http.OnHttpError += (x) =>
			{
				BadResponse?.BeginInvoke(this,x,(obj)=> { },null);
			};
			http.OnDocumentReady += (x) =>
			{
				proxyEndTime = HttpUtil.TimeStamp;
				documentLoaded = true;
				var document = SetDocument(x);
				DocumentReady?.BeginInvoke(this, document, (obj) => { }, null);
			};
		}
		public enum Status
		{
			NoElement,NotBuild,NotReady,Ready
		}
		/// <summary>
		/// 同步方法
		/// </summary>
		/// <param name="document"></param>
		/// <param name="item"></param>
		/// <returns></returns>
		public bool GetResponse( HttpItem item = null)
		{
			return GetResponse(item,false);
		}
		/// <summary>
		/// 异步方法
		/// </summary>
		/// <param name="item"></param>
		/// <param name="CallBack"></param>
		/// <returns></returns>
		public bool GetResponse(HttpItem item = null, Action<HttpApiEvent.HttpDocument> CallBack = null)
		{
			return GetResponse(item,true, CallBack);
		}
		public bool GetResponse(HttpItem item=null,bool syn=false,Action<HttpApiEvent.HttpDocument> CallBack=null)
		{
			if (item != null)
			{
				Item = item;
			}
			proxyBeginTime = HttpUtil.TimeStamp;
			//try
			{
				if (Item.Url == null || Item.Url.Length < 5)
				{
					throw new UrlInvalidException("目标链接错误");
				}
				//http.Option[WinHttpRequestOption.WinHttpRequestOption_EnableHttpsToHttpRedirects] = true;
				//http.Option[WinHttpRequestOption.WinHttpRequestOption_EnableHttp1_1] = true;
				http.Option[WinHttpRequestOption.WinHttpRequestOption_EnableTracing] = 1;
				http.Option[WinHttpRequestOption.WinHttpRequestOption_SslErrorIgnoreFlags] = 13056;
				if(HttpItem.RedirectDisable)http.Option[WinHttpRequestOption.WinHttpRequestOption_EnableRedirects] = false;
				http.Open(Item.Method, Item.Url, syn);
				http.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
				var cookies = Item.Request.Cookies;
				if (cookies != null && cookies.Length > 0) {
					Console.WriteLine("SetCookies " + ID + ":" + cookies);
					http.SetRequestHeader("Cookie", cookies);
				}
				if(Item.Referer!=null)if(Item.Referer!=null)http.SetRequestHeader("Referer", Item.Referer);
				if (Item.IfModified) http.SetRequestHeader("if-Modified","0");
				http.SetRequestHeader("User-Agent", Item.UserAgent);
				if (Item.Host != null) http.SetRequestHeader("Host", Item.Host);
				if (Item.AcceptLanguage != null) http.SetRequestHeader("AcceptLanguage", Item.AcceptLanguage);
				if (Item.Request.HeadersDic != null&& Item.Request.HeadersDic.Count>0)
				{
					foreach (var header in Item.Request.HeadersDic)
					{
						http.SetRequestHeader(header.Key, header.Value);
					}
				}
				http.SetRequestHeader("Set-Cookies", "ClientId:"+ID);
				if(Item.Request.PostData!="")
					http.Send(Item.Request.PostData);
				else
				{
					http.Send();
				}

				if (syn) document = null; else
				{
					var doc = http.ResponseBody;
					var headers = http.GetAllResponseHeaders();
					document = new HttpApiEvent.HttpDocument(doc, headers,Item.Response);
					
					return true;
				}
			}
			//catch (Exception ex)
			//{
			//	Logger.SysLog(ex.Message + '\n' + ex.StackTrace);
			//	document = null;
			//	return true;
			//}
			return true;

		}
		private HttpApiEvent.HttpDocument SetDocument(HttpApiEvent.DocumentReady document)
		{
			return new HttpApiEvent.HttpDocument(ref document,ref this.http,Item.Response);
		}
		public override string ToString()
		{
			var cstr = new StringBuilder();
			cstr.Append("Url").Append(this.Item.Url);
			return cstr.ToString();
		}

	}
	namespace HttpException
	{

		[Serializable]
		public class UrlInvalidException : Exception
		{
			public UrlInvalidException() { }
			public UrlInvalidException(string message) : base(message) { }
			public UrlInvalidException(string message, Exception inner) : base(message, inner) { }
			protected UrlInvalidException(
			  SerializationInfo info,
			  StreamingContext context) : base(info, context) { }
		}
	}
	public class HttpItem
	{
		private static List<string> UserAgentBase=new List<string>() {
			"Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/55.0.2883.87 Safari/537.36 {0}",
			"Mozilla/5.0 (Macintosh; U; Intel Mac OS X 10_6_8; en-us) AppleWebKit/534.50 (KHTML, like Gecko) Version/5.1 Safari/534.50 {0}",
			"Mozilla/5.0 (Windows; U; Windows NT 6.1; en-us) AppleWebKit/534.50 (KHTML, like Gecko) Version/5.1 Safari/534.50$Mozilla/5.0 (compatible; MSIE 9.0; Windows NT 6.1; Trident/5.0 {0}",
		};
		private bool asyn=true;//默认异步
		private string url;
		private string userAgent;
		private bool useRandomAgent=true;
		private string method;
		private string host;
		private string referer;
		private HttpContentItem request;
		private HttpContentItem response;
		private string acceptLanguage;
		private bool ifModified;
		public HttpItem()
		{
			request = new HttpContentItem();
			response = new HttpContentItem();
		}
		/// <summary>
		/// 当存在时会将收到的数据保存到文件
		/// </summary>
		private FileInfo targetFile;

		public string Url { get => url; set => url = value; }
		public string UserAgent { get {
				if (UseRandomAgent)
				{
					var rand = new Random();
					int random = rand.Next(UserAgentBase.Count);
					int randomValue = rand.Next(999);
					return string.Format(UserAgentBase[random], randomValue);
				}
				else
				{
					return userAgent;
				}
			}
			set => userAgent = value; }
		public bool UseRandomAgent { get => useRandomAgent; set => useRandomAgent = value; }
		public string Method { get => method??"get"; set => method = value; }
		public string Host { get => host; set => host = value; }
		public string Referer { get => referer; set => referer = value; }
		public string AcceptLanguage { get => acceptLanguage; set => acceptLanguage = value; }
		public bool IfModified { get => ifModified; set => ifModified = value; }
		public FileInfo TargetFile { get => targetFile; set => targetFile = value; }
		public bool Asyn { get => asyn; set => asyn = value; }
		internal HttpContentItem Request { get => request; set => request = value; }
		internal HttpContentItem Response { get => response; set => response = value; }
		
		public static bool RedirectDisable { get;  set; }
	}
	public class HttpContentItem
	{
		private Dictionary<string, string> cookies;
		private Dictionary<string, string> headers;
		private Dictionary<string, string> postData;
		private byte[] data;
		public HttpContentItem()
		{
			cookies = new Dictionary<string, string>();
			headers = new Dictionary<string, string>();
			postData = new Dictionary<string, string>();
		}
		public string DataString() => DataString(Encoding.UTF8);
		public string DataString(Encoding coding)
		{
			return coding.GetString(Data.ToArray());
		}
		/// <summary>
		/// 同名cookie会被后来的cookie覆盖
		/// </summary>
		/// <param name="initCookies"></param>
		/// <param name="newCookies"></param>
		/// <returns></returns>
		public static string UpdateCookies(string initCookies,string newCookies)
		{
			var item = new HttpContentItem
			{
				Cookies = initCookies + newCookies
			};
			return item.Cookies;
		}
		public static string GetContainerCookies(CookieContainer cookies,string url) {
			var cookiesCollection= cookies.GetCookies(new Uri(url));
			var cstr = new StringBuilder();
			foreach(Cookie cookie in cookiesCollection)
			{
				cstr.Append(cookie.Name).Append(":").Append(cookie.Value).Append(";");
			}
			return cstr.ToString();
		}
		public string Cookies
		{
			get => GetDic("=", ";", ref cookies);
			set => SetDic("=", ";", ref cookies, value?.Replace(":", "=").Replace(" ", ";"));
		}
		public string PostData
		{

			get
			{
				var tmp = GetDic("=", "&", ref postData);
				return tmp.Length > 0 ? tmp.Substring(0, tmp.Length - 1) : tmp;
			}
			set => SetDic("=", "&", ref postData, value);
		}
		public string Headers
		{
			get => GetDic(":", "\r\n", ref headers);
			set => SetDic(": ", "\r\n", ref headers, value);
		}
		public string GetHeader(string key)
		{
			return HeadersDic.ContainsKey(key) ?
			HeadersDic[key] : null;
		}
		public void SetHeaders(string key, string value)
		{
			HeadersDic[key] = value;
		}
		public byte[] Data { get => data; set => data = value; }
		public Dictionary<string, string> CookiesDic { get => cookies; set => cookies = value; }
		public Dictionary<string, string> HeadersDic { get => headers; set => headers = value; }
		public Dictionary<string, string> PostDataDic { get => postData; set => postData = value; }

		public HttpContentItem AddPostData(string key,string value)
		{
			PostDataDic.Add(key, value);
			return this;
		}
		public HttpContentItem RemoveAllPostData()
		{
			PostDataDic.Clear();
			return this;
		}

		private string GetDic(string split, string end,ref Dictionary<string,string> dic)
		{
			
			if (dic == null) return null;
			var cstr = new StringBuilder();
			
			foreach (var item in dic)
			{
				cstr.Append(item.Key).Append(split).Append(item.Value).Append(end);
			}
			return cstr.ToString();
		}
		private void SetDic(string split, string end,ref Dictionary<string,string> dic,string value)
		{
			if (value == null) return;
			string[] items = value.Split(new string[] { end },StringSplitOptions.RemoveEmptyEntries);
			foreach (var item in items)
			{
				if (item.Length < 2) continue;
				string[] info = item.Split(new string[] { split }, StringSplitOptions.RemoveEmptyEntries);
				var NewKey = info[0];
				var NewValue = info.Length == 1 ? "" : info[1];
				dic[NewKey] = NewValue;
			}
		}
	}
}
