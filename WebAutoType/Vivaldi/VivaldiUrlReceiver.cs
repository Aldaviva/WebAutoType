#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WebAutoType.Annotations;

namespace WebAutoType.Vivaldi
{
	public class VivaldiUrlReceiver : IDisposable, INotifyPropertyChanged
	{
		private const int WorkerCount = 4;
		private const ushort ListeningPort = 53372;

		private readonly HttpListener httpServer = new HttpListener();

		private Uri? mostRecentReceivedUrl;

		public Uri? MostRecentReceivedUrl
		{
			get => mostRecentReceivedUrl;
			private set
			{
				if (!Equals(value, mostRecentReceivedUrl))
				{
					mostRecentReceivedUrl = value;
					OnPropertyChanged();
				}
			}
		}

		public void Start()
		{
			const string METHOD_PATH = "/webautotype/activeurl/";
			httpServer.Prefixes.Add(new UriBuilder("http", "127.0.0.1", ListeningPort, METHOD_PATH).ToString());
			httpServer.Prefixes.Add(new UriBuilder("http", "[::1]", ListeningPort, METHOD_PATH).ToString());
			httpServer.IgnoreWriteExceptions = true;
			httpServer.Start();

			var maxWorkersSemaphore = new Semaphore(WorkerCount, WorkerCount);

			Task.Run(() =>
			{
				while (httpServer.IsListening)
				{
					maxWorkersSemaphore.WaitOne();

					httpServer.GetContextAsync().ContinueWith(async incomingConnectionTask =>
					{
						maxWorkersSemaphore.Release();

						HttpListenerContext? requestHolder = null;
						ushort? statusCode = null;

						try
						{
							requestHolder = await incomingConnectionTask;
							statusCode = await HandleRequest(requestHolder);
						}
						catch (Exception)
						{
							statusCode ??= 500;
						}
						finally
						{
							if (requestHolder != null)
							{
								requestHolder.Response.StatusCode = statusCode ?? 204;
								requestHolder.Response.Close();
							}
						}
					});
				}
			});
		}

		private async Task<ushort> HandleRequest(HttpListenerContext requestHolder)
		{
			HttpListenerRequest request = requestHolder.Request;

			try
			{
				if (!request.Url.AbsolutePath.Equals("/webautotype/activeurl/vivaldi", StringComparison.InvariantCultureIgnoreCase))
				{
					return 404;
				}
				else if (request.HttpMethod != WebRequestMethods.Http.Post)
				{
					return 405;
				}
				else if (!request.HasEntityBody || !Regex.IsMatch(request.ContentType, @"^application\/x-www-form-urlencoded(?:$|;(.*)$)", RegexOptions.IgnoreCase))
				{
					return 406;
				}

				EnsureAuthorized(request);
				IDictionary<string, string> body = await ParseFormBody(request);

				if (!(body["activeUrl"] is string activeUrlString) || !Uri.TryCreate(activeUrlString, UriKind.Absolute, out Uri activeUrl))
				{
					return 400;
				}

				MostRecentReceivedUrl = activeUrl;
				return 204;
			}
			catch (AuthenticationException)
			{
				return 403;
			}
		}

		private static void EnsureAuthorized(HttpListenerRequest request)
		{
			if (request.Headers["Origin"] == "chrome-extension://mpognobbkildjkofajifpdfhcoklimli" && // pages send https://blah, browser.html sends the big honking Vivaldi extension URI
			    request.Headers["Sec-Fetch-Site"] == "none") // pages send "cross-site", browser.html sends "none"
			{
				//request is probably coming from the browser.html pages and not some other website, so allow the request
			}
			else
			{
				throw new AuthenticationException("Request headers indicate request may have been sent by XHR in a web page.");
			}
		}

		private static async Task<IDictionary<string, string>> ParseFormBody(HttpListenerRequest request)
		{
			using var streamReader = new StreamReader(request.InputStream, Encoding.UTF8);
			string requestBody = await streamReader.ReadToEndAsync();
			return requestBody.Split('&').Select(pair =>
				{
					string[] keyAndValue = pair.Split("=".ToCharArray(), 2);
					return new KeyValuePair<string, string>(keyAndValue[0], keyAndValue[1]);
				})
				.ToDictionary(pair => Uri.UnescapeDataString(pair.Key), pair => Uri.UnescapeDataString(pair.Value));
		}

		public void Stop()
		{
			httpServer.Stop();
		}

		public void Dispose()
		{
			Stop();
			httpServer.Close();
			((IDisposable)httpServer).Dispose();
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		[NotifyPropertyChangedInvocator]
		protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}