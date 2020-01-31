﻿namespace SIS.HTTP
{
    using System;
    using System.Net;
    using System.Linq;
    using System.Text;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using System.Collections.Generic;

    public class HttpServer : IHttpServer
    {
        //---------------- FIELDS ----------------
        private readonly TcpListener tcpListener;
        private readonly IList<Route> routeTable;
        private readonly IDictionary<string, IDictionary<string, string>> sessions;
        //                           <sid, <sidKey(lang), sidValue(en)>

        //------------- CONSTRUCTORS -------------
        //TODO: Action
        public HttpServer(int port, IList<Route> routeTable)
        {
            this.tcpListener = new TcpListener(IPAddress.Loopback, port);
            this.routeTable = routeTable;
            this.sessions = new Dictionary<string, IDictionary<string, string>>();
        }

        //------------ PUBLIC METHODS ------------
        /// <summary>
        /// Resets the HTTP Server asynchronously.
        /// </summary>
        public async Task ResetAsync()
        {
            this.Stop();
            await this.StartAsync();
        }

        /// <summary>
        /// Starts the HTTP Server asynchronously.
        /// </summary>
        public async Task StartAsync()
        {
            this.tcpListener.Start();
            while (true)
            {
                TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync();
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                Task.Run(() => ProcessClientAsync(tcpClient));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }
        }

        /// <summary>
        /// Stops the HTTP Server.
        /// </summary>
        public void Stop() => this.tcpListener.Stop();

        //------------ PRIVATE METHODS -----------
        /// <summary>
        /// Processes the <see cref="TcpClient"/> asynchronously and returns HTTP Response for the browser.
        /// </summary>
        /// <param name="tcpClient">TCP Client</param>
        /// <returns></returns>
        private async Task ProcessClientAsync(TcpClient tcpClient)
        {
            using NetworkStream networkStream = tcpClient.GetStream();

            try
            {
                //TODO: Store Requests in text files?
                //----------------- HTTP REQUEST -----------------
                byte[] requestBytes = new byte[1_000_000]; //TODO: Use buffer
                int bytesRead = await networkStream.ReadAsync(requestBytes, 0, requestBytes.Length);
                string requestAsString = Encoding.UTF8.GetString(requestBytes, 0, bytesRead);   // GET REQUEST from Firefox in the form of a STRING
                HttpRequest request = new HttpRequest(requestAsString);
                string newSessionId = null;

                Cookie sessionCookie = request.Cookies.FirstOrDefault(x => x.Name == HttpConstants.SessionIdCookieName);
                if (sessionCookie != null && this.sessions.ContainsKey(sessionCookie.Value))
                {
                    request.SessionData = this.sessions[sessionCookie.Value];
                }
                else
                {
                    newSessionId = Guid.NewGuid().ToString();
                    Dictionary<string, string> dictionary = new Dictionary<string, string>();
                    
                    this.sessions.Add(newSessionId, dictionary);
                    request.SessionData = dictionary;
                }

                Console.WriteLine($"{request.Method} {request.Path}");

                //----------------- HTTP RESPONSE ----------------
                Route route = this.routeTable.FirstOrDefault(x => x.HttpMethod == request.Method && x.Path == request.Path); // "/users/login" from Program, list of routes
                HttpResponse response;

                if (route == null)
                {
                    response = new HttpResponse(HttpResponseStatusCode.NotFound, new byte[0]);
                }
                else
                {
                    response = route.Action(request);
                }

                response.Headers.Add(new Header("Server", "SoftUniServer/1.0"));

                if (newSessionId != null)
                {
                    response.Cookies.Add(new ResponseCookie(HttpConstants.SessionIdCookieName, newSessionId) { HttpOnly = true, MaxAge = 30*3600 });
                }

                //----------------- WRITE RESPONSE IN BROWSER ------------------
                byte[] responseBytes = Encoding.UTF8.GetBytes(response.ToString());
                await networkStream.WriteAsync(responseBytes, 0, responseBytes.Length);     // WRITE RESPONSE HEADERS in Firefox
                await networkStream.WriteAsync(response.Body, 0, response.Body.Length);     // WRITE RESPONSE FILE in Firefox
            }
            catch (Exception ex)
            {
                HttpResponse errorResponse = new HttpResponse(HttpResponseStatusCode.InternalServerError, Encoding.UTF8.GetBytes(ex.ToString()));
                errorResponse.Headers.Add(new Header("Content-Type", "text/plain"));

                byte[] responseBytes = Encoding.UTF8.GetBytes(errorResponse.ToString());
                await networkStream.WriteAsync(responseBytes, 0, responseBytes.Length);               // WRITE RESPONSE HEADERS in Firefox
                await networkStream.WriteAsync(errorResponse.Body, 0, errorResponse.Body.Length);     // WRITE RESPONSE FILE in Firefox
            }
        }
    }
}
