using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace Google.CloudPrint.Client
{
    interface IPrintProvider
    {
        CloudPrintGenericResponse Submit(SubmitRequest submitRequest);
        GetJobsResponse GetJobs(string printerid);
        CloudPrintGenericResponse DeleteJob(string jobid);
        CloudPrintPrinterDetails GetPrinterDetails(string printerid, bool getConnectionStatus = true);
        SearchCloudPrintersResponse Search(string q = "", ConnectionStatus connection_status = ConnectionStatus.NONE);
        CloudPrintGenericResponse SharePrinter(string printerid, string emailAddress, bool notifyUser = true);
        CloudPrintGenericResponse UnsharePrinter(string printerid, string emailAddress);
    }

    public enum ConnectionStatus
    {
        NONE,
        ONLINE,
        UNKNOWN,
        OFFLINE,
        DORMANT,
        ALL
    }

    class PrinterClient : IPrintProvider
    {
        private const string DEFAULT_BASE_URI = "https://www.google.com/cloudprint/";
        private const string DEFAULT_SOURCE = "Google-JS";

        protected string BaseUriString { get; set; }
        public Uri BaseUri
        {
            get
            {
                if (string.IsNullOrWhiteSpace(this.BaseUriString))
                {
                    return new Uri(DEFAULT_BASE_URI);
                }

                return new Uri(this.BaseUriString);
            }
        }

        private NetworkCredential _credentials;
        public NetworkCredential Credentials
        {
            get
            {
                return _credentials;
            }
        }

        public string Source { get; set; }


        public PrinterClient(string username, string password, string source = null, Uri baseUri = null)
        {
            _credentials = new NetworkCredential(username, password);
            if (string.IsNullOrWhiteSpace(source)) source = DEFAULT_SOURCE;
            this.Source = source;
            if (null != baseUri) this.BaseUriString = baseUri.ToString();
        }


        private string _authCode;
        private bool CheckAuthorization(out string authCode)
        {
            if (!String.IsNullOrWhiteSpace(_authCode))
            {
                authCode = _authCode;
                return true;
            }

            var result = false;
            authCode = "";

            var queryString = String.Format("https://www.google.com/accounts/ClientLogin?accountType=HOSTED_OR_GOOGLE&Email={0}&Passwd={1}&service=cloudprint&source={2}",
                                             this.Credentials.UserName, this.Credentials.Password, this.Source);
            var request = (HttpWebRequest)WebRequest.Create(queryString);

            var response = (HttpWebResponse)request.GetResponse();
            var responseContent = new StreamReader(response.GetResponseStream()).ReadToEnd();

            var split = responseContent.Split('\n');
            foreach (var s in split)
            {
                var nvsplit = s.Split('=');
                if (nvsplit.Length == 2)
                {
                    if (nvsplit[0] == "Auth")
                    {
                        _authCode = nvsplit[1];
                        authCode = _authCode;
                        result = true;
                    }
                }
            }

            return result;
        }

        private T GetPostDataResult<T>(Uri uri, string methodName, PostData postData = null) where T : class
        {
            string authCode = null;

            if (!CheckAuthorization(out authCode))
            {
                return null;
            }

            T result = null;

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(BuildServiceOperationUriString(uri, methodName));
                request.Method = "POST";

                // Setup the web request
                request.ServicePoint.Expect100Continue = false;

                // Add the headers
                request.Headers.Add("X-CloudPrint-Proxy", Source);
                request.Headers.Add("Authorization", "GoogleLogin auth=" + authCode);

                request.ContentLength = 0;
                request.ContentType = "application/x-www-form-urlencoded";

                if (null != postData)
                {
                    byte[] data = Encoding.UTF8.GetBytes(postData.GetPostData());

                    request.ContentType = "multipart/form-data; boundary=" + postData.Boundary;
                    request.ContentLength = data.Length;

                    Stream stream = request.GetRequestStream();
                    stream.Write(data, 0, data.Length);
                    stream.Close();
                }

                var response = (HttpWebResponse)request.GetResponse();
                //var responseContent = new StreamReader(response.GetResponseStream()).ReadToEnd();

                var serializer = new DataContractJsonSerializer(typeof(T));
                //var ms = new MemoryStream(Encoding.UTF8.GetBytes(responseContent));
                result = serializer.ReadObject(response.GetResponseStream()) as T;
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Unable to connect to the Google Cloud Print service.", ex);
            }

            return result;
        }

        private Uri BuildServiceOperationUriString(Uri uri, string methodName)
        {
            if (null == uri) return uri;

            string strUri = uri.ToString().TrimEnd('/');

            return new Uri(string.Format("{0}/{1}", strUri, methodName));
        }


        public Task<CloudPrintGenericResponse> SubmitAsync(SubmitRequest submitRequest)
        {
            return Task<CloudPrintGenericResponse>.Factory.StartNew(() =>
            {
                return Submit(submitRequest);
            });
        }

        public Task<CloudPrintGenericResponse> SharePrinterAsync(string printerid, string emailAddress, bool skipNotifyUser = false)
        {
            return Task<CloudPrintGenericResponse>.Factory.StartNew(() =>
            {
                return SharePrinter(printerid, emailAddress, skipNotifyUser);
            });
        }

        public Task<CloudPrintGenericResponse> UnsharePrinterAsync(string printerid, string emailAddress)
        {
            return Task<CloudPrintGenericResponse>.Factory.StartNew(() =>
            {
                return UnsharePrinter(printerid, emailAddress);
            });
        }

        public Task<GetJobsResponse> GetJobsAsync(string printerid = null)
        {
            return Task<GetJobsResponse>.Factory.StartNew(() =>
            {
                return GetJobs(printerid);
            });
        }

        public Task<CloudPrintGenericResponse> DeleteJobAsync(string jobid)
        {
            return Task<CloudPrintGenericResponse>.Factory.StartNew(() =>
            {
                return DeleteJob(jobid);
            });
        }

        public Task<CloudPrintPrinterDetails> GetPrinterDetailsAsync(string printerid, bool getConnectionStatus = true)
        {
            return Task<CloudPrintPrinterDetails>.Factory.StartNew(() =>
            {
                return GetPrinterDetails(printerid, getConnectionStatus);
            });
        }

        public Task<SearchCloudPrintersResponse> SearchAsync(string query = "", ConnectionStatus connectionStatus = ConnectionStatus.NONE)
        {
            return Task<SearchCloudPrintersResponse>.Factory.StartNew(() =>
            {
                return Search(query, connectionStatus);
            });
        }



        public CloudPrintGenericResponse Submit(SubmitRequest submitRequest)
        {
            CloudPrintGenericResponse response = new CloudPrintGenericResponse();

            if (null == submitRequest || String.IsNullOrWhiteSpace(submitRequest.contentType) || null == submitRequest.content)
            {
                throw new ApplicationException("Invalid parameters, make sure that the request parameters and content type have been specified.");
            }

            try
            {
                var p = new PostData();
                p.Params.Add(new PostDataParam
                {
                    Name = "printerid",
                    Value = submitRequest.printerid,
                    Type = PostDataParamType.Field
                });

                if (!String.IsNullOrWhiteSpace(submitRequest.contentType))
                {
                    p.Params.Add(new PostDataParam
                    {
                        Name = "contentType",
                        Value = submitRequest.contentType,
                        Type = PostDataParamType.Field
                    });
                }

                if (null != submitRequest.content)
                {
                    var b64 = Convert.ToBase64String(submitRequest.content);

                    if (!String.IsNullOrWhiteSpace(b64))
                    {
                        string mimeType = string.IsNullOrWhiteSpace(submitRequest.contentType) ? submitRequest.contentType : "text/plain";

                        p.Params.Add(new PostDataParam
                        {
                            Name = "content",
                            Type = PostDataParamType.Field,
                            Value = "data:" + mimeType + ";base64," + b64
                        });
                    }
                }

                if (!String.IsNullOrWhiteSpace(submitRequest.title))
                {
                    p.Params.Add(new PostDataParam
                    {
                        Name = "title",
                        Value = submitRequest.title,
                        Type = PostDataParamType.Field
                    });
                }

                if (!String.IsNullOrWhiteSpace(submitRequest.capabilities))
                {
                    p.Params.Add(new PostDataParam
                    {
                        Name = "capabilities",
                        Value = submitRequest.capabilities,
                        Type = PostDataParamType.Field
                    });
                }

                if (!String.IsNullOrWhiteSpace(submitRequest.tag))
                {
                    p.Params.Add(new PostDataParam
                    {
                        Name = "tag",
                        Value = submitRequest.tag,
                        Type = PostDataParamType.Field
                    });
                }

                CloudPrintGenericResponse result
                    = GetPostDataResult<CloudPrintGenericResponse>(this.BaseUri, "submit", p);

                if (null == result)
                {
                    return response;
                }

                return result;
            }
            catch (Exception ex)
            {
                response.message = ex.Message;
                response.success = false;
                return response;
            }
        }

        public CloudPrintGenericResponse SharePrinter(string printerid, string emailAddress, bool skipNotifyUser = false)
        {
            CloudPrintGenericResponse response = new CloudPrintGenericResponse();

            try
            {
                var p = new PostData();

                p.Params.Add(new PostDataParam { Name = "printerid", Value = printerid, Type = PostDataParamType.Field });
                p.Params.Add(new PostDataParam { Name = "email", Value = emailAddress, Type = PostDataParamType.Field });
                p.Params.Add(new PostDataParam { Name = "role", Value = "APPENDER", Type = PostDataParamType.Field });
                p.Params.Add(new PostDataParam { Name = "skip_notification", Value = skipNotifyUser.ToString(), Type = PostDataParamType.Field });

                if (0 >= p.Params.Count()) p = null;

                CloudPrintGenericResponse result
                    = GetPostDataResult<CloudPrintGenericResponse>(this.BaseUri, "share", p);

                if (null == result)
                {
                    return response;
                }

                return result;
            }
            catch (Exception ex)
            {
                response.message = ex.Message;
                response.success = false;
                return response;
            }
        }

        public CloudPrintGenericResponse UnsharePrinter(string printerid, string emailAddress)
        {
            CloudPrintGenericResponse response = new CloudPrintGenericResponse();

            try
            {
                var p = new PostData();

                p.Params.Add(new PostDataParam { Name = "printerid", Value = printerid, Type = PostDataParamType.Field });
                p.Params.Add(new PostDataParam { Name = "email", Value = emailAddress, Type = PostDataParamType.Field });

                if (0 >= p.Params.Count()) p = null;

                CloudPrintGenericResponse result
                    = GetPostDataResult<CloudPrintGenericResponse>(this.BaseUri, "unshare", p);

                if (null == result)
                {
                    return response;
                }

                return result;
            }
            catch (Exception ex)
            {
                response.message = ex.Message;
                response.success = false;
                return response;
            }
        }


        public GetJobsResponse GetJobs(string printerid = null)
        {
            GetJobsResponse response = new GetJobsResponse();

            try
            {
                var p = new PostData();

                if (!String.IsNullOrWhiteSpace(printerid))
                {
                    p.Params.Add(new PostDataParam { Name = "printerid", Value = printerid, Type = PostDataParamType.Field });
                }

                if (0 >= p.Params.Count()) p = null;

                GetJobsResponse result
                    = GetPostDataResult<GetJobsResponse>(this.BaseUri, "jobs", p);

                if (null == result)
                {
                    return response;
                }

                return result;
            }
            catch (Exception ex)
            {
                response.message = ex.Message;
                response.success = false;
                return response;
            }
        }

        public CloudPrintGenericResponse DeleteJob(string jobid)
        {
            CloudPrintGenericResponse response = new CloudPrintGenericResponse();

            try
            {
                var p = new PostData();

                p.Params.Add(new PostDataParam { Name = "jobid", Value = jobid, Type = PostDataParamType.Field });

                CloudPrintGenericResponse result
                    = GetPostDataResult<CloudPrintGenericResponse>(this.BaseUri, "deletejob", p);

                if (null == result)
                {
                    return response;
                }

                return result;
            }
            catch (Exception ex)
            {
                response.message = ex.Message;
                response.success = false;
                return response;
            }
        }

        public CloudPrintPrinterDetails GetPrinterDetails(string printerid, bool getConnectionStatus = false)
        {
            CloudPrintPrinterDetails response = new CloudPrintPrinterDetails();

            try
            {
                var p = new PostData();
                p.Params.Add(new PostDataParam { Name = "printerid", Value = printerid, Type = PostDataParamType.Field });

                if (true == getConnectionStatus)
                {
                    p.Params.Add(new PostDataParam { Name = "printer_connection_status", Value = getConnectionStatus.ToString(), Type = PostDataParamType.Field });
                }

                CloudPrintPrinterDetails result
                    = GetPostDataResult<CloudPrintPrinterDetails>(this.BaseUri, "printer", p);

                if (null == result)
                {
                    return response;
                }

                return result;
            }
            catch (Exception ex)
            {
                response.message = ex.Message;
                response.success = false;
                return response;
            }
        }


        public SearchCloudPrintersResponse Search(string query = "", ConnectionStatus connection_status = ConnectionStatus.NONE)
        {
            SearchCloudPrintersResponse response = new SearchCloudPrintersResponse();

            try
            {
                var postData = new PostData();

                if (!String.IsNullOrWhiteSpace(query))
                {
                    postData.Params.Add(new PostDataParam()
                    {
                        Name = "q",
                        Value = query,
                        Type = PostDataParamType.Field
                    });
                }

                if (ConnectionStatus.NONE != connection_status)
                {
                    postData.Params.Add(new PostDataParam()
                    {
                        Name = "connection_status",
                        Value = connection_status.ToString(),
                        Type = PostDataParamType.Field
                    });
                }

                if (0 >= postData.Params.Count()) postData = null;

                SearchCloudPrintersResponse result
                    = GetPostDataResult<SearchCloudPrintersResponse>(this.BaseUri, "search", postData);

                if (null == result)
                {
                    return response;
                }

                return result;
            }
            catch (Exception ex)
            {
                response.message = ex.Message;
                response.success = false;
                return response;
            }
        }
    }


    [DataContract]
    public class SearchCloudPrintersResponse : CloudPrintGenericResponse
    {
        [DataMember]
        public List<CloudPrinter> printers { get; set; }
    }

    [DataContract]
    public class CloudPrinter
    {
        [DataMember]
        public string id { get; set; }

        [DataMember]
        public string name { get; set; }

        [DataMember]
        public string description { get; set; }

        [DataMember]
        public string proxy { get; set; }

        [DataMember]
        public string status { get; set; }

        [DataMember]
        public string capsHash { get; set; }

        [DataMember]
        public string createTime { get; set; }

        [DataMember]
        public string updateTime { get; set; }

        [DataMember]
        public string accessTime { get; set; }

        [DataMember]
        public bool confirmed { get; set; }

        [DataMember]
        public int numberOfDocuments { get; set; }

        [DataMember]
        public int numberOfPages { get; set; }
    }


    [DataContract]
    public class CloudPrinterCapabilityOption
    {
        [DataMember]
        public string name { get; set; }
        [DataMember(Name = "psk:DisplayName")]
        public string DisplayName { get; set; }
        [DataMember]
        public string @default { get; set; }
        [DataMember(Name = "psk:ResolutionX")]
        public string ResolutionX { get; set; }
        [DataMember(Name = "psk:ResolutionY")]
        public string ResolutionY { get; set; }
        [DataMember(Name = "psk:MediaSizeWidth")]
        public string MediaSizeWidth { get; set; }
        [DataMember(Name = "psk:MediaSizeHeight")]
        public string MediaSizeHeight { get; set; }
    }

    [DataContract]
    public class CloudPrinterCapability
    {
        [DataMember]
        public string name { get; set; }
        [DataMember(Name = "psf:SelectionType")]
        public string SelectionType { get; set; }
        [DataMember(Name = "psk:DisplayName")]
        public string DisplayName { get; set; }
        [DataMember(Name = "psf:DataType")]
        public string DataType { get; set; }
        [DataMember(Name = "psf:UnitType")]
        public string UnitType { get; set; }
        [DataMember(Name = "psf:DefaultValue")]
        public string DefaultValue { get; set; }
        [DataMember(Name = "psf:MinValue")]
        public string MinValue { get; set; }
        [DataMember(Name = "psf:MaxValue")]
        public string MaxValue { get; set; }
        [DataMember]
        public string type { get; set; }
        [DataMember]
        public List<CloudPrinterCapabilityOption> options { get; set; }
    }

    [DataContract]
    public class CloudPrinterAccess
    {
        [DataMember]
        public string membership { get; set; }
        [DataMember]
        public string email { get; set; }
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public string role { get; set; }
        [DataMember]
        public string type { get; set; }
        [DataMember]
        public string is_pending { get; set; }
    }


    [DataContract]
    public class CloudPrintPrinterDetails : CloudPrintGenericResponse
    {
        [DataMember]
        public List<CloudPrintPrinterDetailItem> printers { get; set; }
    }


    [DataContract]
    public class CloudPrintPrinterDetailItem
    {
        [DataMember]
        public string createTime { get; set; }

        [DataMember]
        public string model { get; set; }

        [DataMember]
        public string accessTime { get; set; }

        [DataMember]
        public string gcpVersion { get; set; }

        [DataMember]
        public string ownerId { get; set; }

        [DataMember]
        public string isTosAccepted { get; set; }

        [DataMember]
        public string type { get; set; }

        [DataMember]
        public string id { get; set; }

        [DataMember]
        public string description { get; set; }

        [DataMember]
        public string defaultDisplayName { get; set; }

        [DataMember]
        public string name { get; set; }

        [DataMember]
        public string proxy { get; set; }

        [DataMember]
        public string capsFormat { get; set; }

        [DataMember]
        public List<string> tags { get; set; }

        [DataMember]
        public string updateUrl { get; set; }

        [DataMember]
        public string supportedContentTypes { get; set; }

        [DataMember]
        public string status { get; set; }

        [DataMember]
        public string updateTime { get; set; }

        [DataMember]
        public string capsHash { get; set; }

        [DataMember]
        public List<CloudPrinterAccess> access { get; set; }

        [DataMember]
        public string manufacturer { get; set; }

        [DataMember]
        public string connectionStatus { get; set; }

        [DataMember]
        public string uuid { get; set; }

        [DataMember]
        public List<CloudPrinterCapability> capabilities { get; set; }

        [DataMember]
        public string displayName { get; set; }
    }

    [DataContract]
    public class GetJobsResponse : CloudPrintGenericResponse
    {
        [DataMember]
        public List<CloudPrintJob> jobs { get; set; }
    }

    [DataContract]
    public class CloudPrintJob
    {
        [DataMember]
        public string id { get; set; }

        [DataMember]
        public string printerid { get; set; }

        [DataMember]
        public string printerName { get; set; }

        [DataMember]
        public string printerType { get; set; }

        [DataMember]
        public string message { get; set; }

        [DataMember]
        public string numberOfPages { get; set; }

        [DataMember]
        public string ownerId { get; set; }

        [DataMember]
        public string title { get; set; }

        [DataMember]
        public string contentType { get; set; }

        [DataMember]
        public string fileUrl { get; set; }

        [DataMember]
        public string ticketUrl { get; set; }

        [DataMember]
        public string createTime { get; set; }

        [DataMember]
        public string updateTime { get; set; }

        [DataMember]
        public string status { get; set; }

        [DataMember]
        public List<string> tags { get; set; }
    }

    [DataContract]
    public class CloudPrintRequestDetails
    {
        [DataMember]
        public string time { get; set; }
        [DataMember]
        public List<String> users { get; set; }
        [DataMember]
        public Dictionary<string, string> @params { get; set; }
        [DataMember]
        public string user { get; set; }
    }


    [DataContract]
    public class CloudPrintGenericResponse
    {
        [DataMember]
        public CloudPrintRequestDetails request { get; set; }

        [DataMember]
        public bool success { get; set; }

        [DataMember]
        public string errorCode { get; set; }

        [DataMember]
        public string message { get; set; }

        [DataMember]
        public string xsrf_token { get; set; }
    }

    [DataContract]
    public class CloudPrintGenericRequest
    {
        [DataMember]
        public string printerid { get; set; }
    }

    [DataContract]
    public class SubmitRequest : CloudPrintGenericRequest
    {
        [DataMember]
        public string title { get; set; }

        [DataMember]
        public string capabilities { get; set; }

        [DataMember]
        public byte[] content { get; set; }

        [DataMember]
        public string contentType { get; set; }

        [DataMember]
        public string tag { get; set; }
    }

}