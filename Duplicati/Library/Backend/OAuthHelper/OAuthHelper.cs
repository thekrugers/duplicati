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
        private string m_servicename;
        private DateTime m_tokenExpires = DateTime.UtcNow;

        private const string OAUTH_HANDLER_DOMAIN = "oauth-dot-lightstone-01.appspot.com";
        public const string DUPLICATI_OAUTH_SERVICE = "https://" + OAUTH_HANDLER_DOMAIN + "/refresh";
        private const string OAUTH_LOGIN_URL_TEMPLATE = "https://" + OAUTH_HANDLER_DOMAIN + "/type={0}";

        public static string OAUTH_LOGIN_URL(string modulename) { return string.Format("https://" + OAUTH_HANDLER_DOMAIN + "/type={0}", modulename); }

        public string OAuthLoginUrl { get; private set; }
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
            var req = CreateRequest(DUPLICATI_OAUTH_SERVICE);
            req.Headers["X-AuthID"] = m_authid;

            return ReadJSONResponse<T>(req);
        }

        public override HttpWebRequest CreateRequest(string url, string method = null)
        {
            var r = base.CreateRequest(url, method);
            if (AutoAuthHeader && !DUPLICATI_OAUTH_SERVICE.Equals(url))
                r.Headers["Authorization"] = string.Format("Bearer {0}", AccessToken);
            return r;
        } 

        public string AccessToken
        {
            get
            {
                if (m_token == null || m_tokenExpires < DateTime.UtcNow)
                {
                    try
                    {
                        var res = GetTokenResponse<OAuth_Service_Response>();

                        m_tokenExpires = DateTime.UtcNow.AddSeconds(res.expires - 30);
                        m_token = res.access_token;
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
                            }
                        }

                        throw new Exception(Strings.OAuthHelper.AuthorizationFailure(msg, OAuthLoginUrl), ex);
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

