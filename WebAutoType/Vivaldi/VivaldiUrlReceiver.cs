#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KeePass.Plugins;
using KeePassLib.Utility;
using Sodium;
using WebAutoType.Annotations;

namespace WebAutoType.Vivaldi
{
	public class VivaldiUrlReceiver : IDisposable, INotifyPropertyChanged
	{
		private const int                     WorkerCount          = 4;
		private const ushort                  ListeningPort        = 53372;
		private const string                  PrivateKeyConfigName = "WebAutoType.VivaldiUrlReceiver.PrivateKey";
		private const string                  MethodPath           = "/webautotype/activeurl/";
		private const Utilities.Base64Variant Base64Variant        = Utilities.Base64Variant.UrlSafeNoPadding;

		private readonly HttpListener httpServer = new HttpListener();

		private Uri?    mostRecentReceivedUrl;
		private byte[]? privateKey;

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

		public void Start(IPluginHost pluginHost)
		{
			SodiumCore.Init();
			string? privateKeyBase64 = pluginHost.CustomConfig.GetString(PrivateKeyConfigName);
			if (privateKeyBase64 != null)
			{
				privateKey = Utilities.Base64ToBinary(StrUtil.Deobfuscate(privateKeyBase64), null, Base64Variant);
			}
			else
			{
				privateKey = SecretBox.GenerateKey();
				privateKeyBase64 = Utilities.BinaryToBase64(privateKey, Base64Variant);
				pluginHost.CustomConfig.SetString(PrivateKeyConfigName, StrUtil.Obfuscate(privateKeyBase64));
				MessageBox.Show($"Generated new private key for WebAutoType to receive active URL from Vivaldi. Please set it in Vivaldi:\n\n1. Go to vivaldi://extensions\n2. Ensure Developer Mode is enabled.\n3. If WebAutoTypeVivaldiExtension is not installed, download the .crx file from https://github.com/Aldaviva/WebAutoTypeVivaldiExtension/releases/latest and drag it into the Extensions page. If drag and drop doesn't work, try going to vivaldi://extensions again.\n4. Under WebAutoType › Inspect Views, click background.html\n5. Go to the Console tab of DevTools.\n6. Paste and run the following command:\n\nchrome.storage.local.set({{ 'WebAutoType.VivaldiUrlReceiver.PrivateKey': '{privateKeyBase64}' }})\n\n(hint: you can copy this dialog text with Ctrl+C)\n7. You can disable Developer Mode if you want.", "WebAutoType", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}

			httpServer.Prefixes.Add(new UriBuilder("http", "127.0.0.1", ListeningPort, MethodPath).ToString());
			httpServer.Prefixes.Add(new UriBuilder("http", "[::1]", ListeningPort, MethodPath).ToString());
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

				string? ciphertextBase64 = body["activeUrlEncrypted"];
				string? nonceBase64 = body["nonce"];

				if (ciphertextBase64 == null || nonceBase64 == null)
				{
					return 400;
				}
				else if (privateKey == null)
				{
					return 500;
				}

				byte[] ciphertext = Utilities.Base64ToBinary(ciphertextBase64, null, Base64Variant);
				byte[] nonce = Utilities.Base64ToBinary(nonceBase64, null, Base64Variant);

				byte[] cleartext;
				try
				{
					cleartext = SecretAeadXChaCha20Poly1305.Decrypt(ciphertext, nonce, privateKey);
				}
				catch (Exception e) when (!(e is OutOfMemoryException))
				{
					return 400;
				}

				string activeUrlString = Encoding.UTF8.GetString(cleartext);

				if (!Uri.TryCreate(activeUrlString, UriKind.Absolute, out Uri activeUrl))
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
			if (request.Headers["Origin"] == "chrome-extension://bpikannkbkpbglmojcajbepbemdlpdhc" && // pages send https://blah, WebAutoType extension background.html sends the big honking extension URI
				request.Headers["Sec-Fetch-Site"] == "none")                                          // pages send "cross-site", background.html sends "none"
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
