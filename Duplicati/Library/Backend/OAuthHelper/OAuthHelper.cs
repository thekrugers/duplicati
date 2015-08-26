﻿//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Net;
using Duplicati.Library.Utility;
using System.Collections.Generic;
using System.Web;
namespace Duplicati.Library
{
    public class OAuthHelper : JSONWebHelper
    {
        private string m_token;
        private string m_authid;
        private DateTime m_tokenExpires = DateTime.UtcNow;

        private static string _override_server = null;

        public static string OAUTH_SERVER 
        { 
            get { return string.IsNullOrWhiteSpace(_override_server) ? DUPLICATI_OAUTH_SERVICE : _override_server; }
            set { _override_server = value; }
        }

        private const string OAUTH_HANDLER_DOMAIN = "oauth-dot-lightstone-01.appspot.com";
        public const string DUPLICATI_OAUTH_SERVICE = "https://" + OAUTH_HANDLER_DOMAIN + "/refresh";
        private const string OAUTH_LOGIN_URL_TEMPLATE = "https://" + OAUTH_HANDLER_DOMAIN + "/type={0}";

        public static string OAUTH_LOGIN_URL(string modulename) 
        {
            var u = new Library.Utility.Uri(OAUTH_SERVER);
            var addr = u.SetPath("").SetQuery((u.Query ?? "") + (string.IsNullOrWhiteSpace(u.Query) ? "" : "&") + "type={0}");
            return string.Format(addr.ToString(), modulename); 
        }

        public bool AutoAuthHeader { get; set; }

        public OAuthHelper(string authid, string servicename, string useragent = null)
            : base(useragent)
        {
            m_authid = authid;
            OAuthLoginUrl = OAUTH_LOGIN_URL(servicename);

            if (string.IsNullOrEmpty(authid))
                throw new Exception(Strings.OAuthHelper.MissingAuthID(OAuthLoginUrl));
        }

        public T GetJSONData<T>(string url, Action<HttpWebRequest> setup = null, Action<AsyncHttpRequest> setupreq = null)
        {
            var req = CreateRequest(url);

            if (setup != null)
                setup(req);

            var areq = new AsyncHttpRequest(req);

            if (setupreq != null)
                setupreq(areq);

            return ReadJSONResponse<T>(areq);
        }

        public T GetTokenResponse<T>()
        {
            var req = CreateRequest(OAUTH_SERVER);
            req.Headers["X-AuthID"] = m_authid;
            req.Timeout = (int)TimeSpan.FromSeconds(25).TotalMilliseconds;

            return ReadJSONResponse<T>(req);
        }

        public override HttpWebRequest CreateRequest(string url, string method = null)
        {
            var r = base.CreateRequest(url, method);
            if (AutoAuthHeader && !OAUTH_SERVER.Equals(url))
                r.Headers["Authorization"] = string.Format("Bearer {0}", AccessToken);
            return r;
        } 

        public string AccessToken
        {
            get
            {
                if (m_token == null || m_tokenExpires < DateTime.UtcNow)
                {
                    var retries = 0;

                    while(true)
                    {
                        try
                        {
                            var res = GetTokenResponse<OAuth_Service_Response>();

                            m_tokenExpires = DateTime.UtcNow.AddSeconds(res.expires - 30);
                            return m_token = res.access_token;
                        }
                        catch (Exception ex)
                        {
                            var msg = ex.Message;
                            if (ex is WebException)
                            {
                                var resp = ((WebException)ex).Response as HttpWebResponse;
                                if (resp != null)
                                {
                                    msg = resp.Headers["X-Reason"];
                                    if (string.IsNullOrWhiteSpace(msg))
                                        msg = resp.StatusDescription;
                                    
                                    if (resp.StatusCode == HttpStatusCode.ServiceUnavailable)
                                    {
                                        if (msg == resp.StatusDescription)
                                            throw new Exception(Strings.OAuthHelper.OverQuotaError);
                                        else
                                            throw new Exception(Strings.OAuthHelper.AuthorizationFailure(msg, OAuthLoginUrl), ex);
                                    }
                                }
                            }

                            if (retries >= 5)
                                throw new Exception(Strings.OAuthHelper.AuthorizationFailure(msg, OAuthLoginUrl), ex);

                            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(Math.Pow(2, retries)));
                            retries++;
                        }
                    }
                }

                return m_token;
            }
        }



        private class OAuth_Service_Response
        {
            public string access_token { get; set; }
            [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
            public int expires { get; set; }
        }

    }
}

