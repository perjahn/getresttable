using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace getresttable
{
    class TableRow
    {
        public string Key { get; set; } = string.Empty;
        public string[] Data { get; set; } = { };
    }

    class Program
    {
        static bool verbose = false;

        static async Task<int> Main(string[] args)
        {
            var parsedArgs = args.ToList();

            var authtoken = ExtractArgumentValue(parsedArgs, "-a");
            var cookie = ExtractArgumentValue(parsedArgs, "-c");
            var excludeFields = ExtractArgumentArray(parsedArgs, "-e");
            verbose = ExtractArgumentFlag(parsedArgs, "-verbose");

            if (parsedArgs.Count != 2)
            {
                Log("Usage: getresttable <url> <keyfieldname> [-a authtoken] [-c cookie] [-e excludefield1,excludefield2,...] [-verbose]", ConsoleColor.Red);
                return 1;
            }

            var url = parsedArgs[0];
            var keyfieldname = parsedArgs[1];

            int result = await GetRestTable(url, keyfieldname, authtoken, cookie, excludeFields);

            return result;
        }

        static bool ExtractArgumentFlag(List<string> args, string flagname)
        {
            int index = args.IndexOf(flagname);
            if (index < 0)
            {
                return false;
            }

            args.RemoveAt(index);
            return true;
        }

        static string? ExtractArgumentValue(List<string> args, string flagname)
        {
            int index = args.IndexOf(flagname);
            if (index < 0 || index >= args.Count - 1)
            {
                return null;
            }

            var value = args[index + 1];
            args.RemoveRange(index, 2);
            return value;
        }

        static string[] ExtractArgumentArray(List<string> args, string flagname)
        {
            int index = args.IndexOf(flagname);
            if (index < 0 || index >= args.Count - 1)
            {
                return new string[] { };
            }

            var values = args[index + 1];
            args.RemoveRange(index, 2);
            return values.Split(',');
        }

        static async Task<int> GetRestTable(string url, string keyfieldname, string? authtoken, string? cookie, string[] excludeFields)
        {
            string baseaddress = GetBaseAddress(url);
            if (baseaddress == string.Empty)
            {
                Log($"Invalid url: '{url}'", ConsoleColor.Red);
                return 1;
            }

            string subpath = GetSubPath(url);

            var handler = new HttpClientHandler { UseCookies = false };
            var client = new HttpClient(handler);

            if (authtoken != null)
            {
                if (authtoken.Contains(':'))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(authtoken)));
                }
                else
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authtoken);
                }
            }
            client.BaseAddress = new Uri(baseaddress);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));


            var objects = await GetObjects(client, subpath, cookie);

            Log($"Got {objects.Length} valid objects.", ConsoleColor.Green);

            if (objects.Length == 0)
            {
                return 0;
            }

            var allHeaders = new List<string>();

            foreach (JObject jobject in objects)
            {
                foreach (var token in jobject.Children())
                {
                    if (token is JProperty jproperty)
                    {
                        var name = jproperty.Name;
                        if (!excludeFields.Contains(name) && !allHeaders.Contains(name))
                        {
                            allHeaders.Add(name);
                        }
                    }
                    else
                    {
                        Log($"Ignoring invalid object: '{token.ToString()}'", ConsoleColor.Yellow);
                    }
                }
            }

            var headerRow = new TableRow();
            headerRow.Key = keyfieldname;
            headerRow.Data = allHeaders.Where(h => h != keyfieldname).OrderBy(h => h).ToArray();

            var allObjects = new List<TableRow>();
            allObjects.Add(headerRow);

            int rowcount = 0;
            foreach (JObject jobject in objects)
            {
                var keyValue = jobject[keyfieldname];
                if (keyValue == null)
                {
                    Log($"Ignoring invalid object, missing '{keyfieldname}' field: '{jobject.ToString()}'");
                    continue;
                }

                var key = keyValue.Value<string>();

                var row = new TableRow();
                row.Key = key;

                var values = new List<string>();

                foreach (var fieldname in headerRow.Data)
                {
                    var value = jobject[fieldname];
                    if (value == null)
                    {
                        values.Add(string.Empty);
                    }
                    else
                    {
                        string s;
                        if (value is JObject || value is JArray)
                        {
                            s = value.ToString();
                        }
                        else
                        {
                            s = value.Value<string>();
                        }
                        //var s = value is JObject ? value.ToString() : value.Value<string>();

                        s = s ?? string.Empty;
                        s = s.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace("  ", " ").Replace("  ", " ").Replace("  ", " ");

                        values.Add(GetFirst(s, 25));
                    }
                }

                row.Data = values.ToArray();

                allObjects.Add(row);
                rowcount++;
            }

            Log($"Valid object count: {allObjects.Count - 1}");

            ShowTable(allObjects.ToArray());

            return 0;
        }

        static void ShowTable(TableRow[] rows)
        {
            if (rows.Length == 0)
            {
                return;
            }

            foreach (var row in rows)
            {
                for (int col = 0; col < row.Data.Length; col++)
                {
                    if (row.Data[col].Length > 1)
                    {
                        row.Data[col] = row.Data[col];
                    }
                }
            }


            string separator = new string(' ', 2);
            int[] maxwidths = GetMaxWidths(rows);

            bool headerRow = true;

            foreach (var row in rows)
            {
                var output = new StringBuilder();

                output.AppendFormat("{0,-" + maxwidths[0] + "}", row.Key);

                for (int col = 0; col < row.Data.Length; col++)
                {
                    output.AppendFormat("{0}{1,-" + maxwidths[col + 1] + "}", separator, row.Data[col]);
                }

                string textrow = output.ToString().TrimEnd();

                if (headerRow)
                {
                    Log(textrow);
                    headerRow = false;
                }
                else
                {
                    if (textrow.Contains('*'))
                    {
                        Log(textrow);
                    }
                    else
                    {
                        Log(textrow, ConsoleColor.Yellow);
                    }
                }
            }
        }

        static string GetFirst(string text, int chars)
        {
            return text.Length < chars ? text : text.Substring(0, chars - 3) + "...";
        }

        static int Compare(string[] arr1, string[] arr2)
        {
            foreach (var element in arr1.Zip(arr2))
            {
                int result = string.Compare(element.First, element.Second, StringComparison.OrdinalIgnoreCase);
                if (result < 0)
                {
                    return -1;
                }
                if (result > 0)
                {
                    return 1;
                }
            }
            if (arr1.Length < arr2.Length)
            {
                return -1;
            }
            if (arr1.Length > arr2.Length)
            {
                return 1;
            }

            return 0;
        }

        static int[] GetMaxWidths(TableRow[] rows)
        {
            if (rows.Length == 0)
            {
                return Array.Empty<int>();
            }

            int[] maxwidths = new int[rows[0].Data.Length + 1];

            for (var row = 0; row < rows.Length; row++)
            {
                if (row == 0)
                {
                    maxwidths[0] = rows[row].Key.Length;
                }
                else
                {
                    if (rows[row].Key.Length > maxwidths[0])
                    {
                        maxwidths[0] = rows[row].Key.Length;
                    }
                }
                for (var col = 0; col < rows[0].Data.Length && col < rows[row].Data.Length; col++)
                {
                    if (row == 0)
                    {
                        maxwidths[col + 1] = rows[row].Data[col].Length;
                    }
                    else
                    {
                        if (rows[row].Data[col].Length > maxwidths[col + 1])
                        {
                            maxwidths[col + 1] = rows[row].Data[col].Length;
                        }
                    }
                }
            }

            return maxwidths;
        }

        static async Task<JObject[]> GetObjects(HttpClient client, string url, string? cookie)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (cookie != null)
            {
                request.Headers.Add("Cookie", cookie);
            }

            LogVerbose($"Request {client.BaseAddress}{url}");

            using var response = await client.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Log(json, ConsoleColor.Red);
            }
            response.EnsureSuccessStatusCode();

            if (!TryParseJToken(json, out JToken jtoken))
            {
                File.WriteAllText("error.html", json);
                throw new Exception($"Couldn't parse json (saved to error.html): {GetFirstCharacters(json, 100)}");
            }

            LogVerbose($"Response: >>>{jtoken.ToString()}<<<");

            var list = new List<JObject>();

            if (jtoken is JObject jobject)
            {
                foreach (var jtoken2 in jobject)
                {
                    if (jtoken2.Value is JArray jarray)
                    {
                        foreach (var jtoken3 in jarray)
                        {
                            if (jtoken3 is JObject jobject2)
                            {
                                list.Add(jobject2);
                            }
                        }
                    }
                }
            }
            if (jtoken is JArray jarray2)
            {
                foreach (var jtoken2 in jarray2)
                {
                    if (jtoken2 is JObject jobject2)
                    {
                        list.Add(jobject2);
                    }
                }
            }

            return list.ToArray();
        }

        static string GetBaseAddress(string url)
        {
            int end;
            if (url.StartsWith("https://"))
            {
                end = url.IndexOf("/", 8);
            }
            else if (url.StartsWith("http://"))
            {
                end = url.IndexOf("/", 7);
            }
            else
            {
                return string.Empty;
            }

            if (end < 0)
            {
                return url;
            }
            return url.Substring(0, end);
        }

        static string GetSubPath(string url)
        {
            int end;
            if (url.StartsWith("https://"))
            {
                end = url.IndexOf("/", 8);
            }
            else if (url.StartsWith("http://"))
            {
                end = url.IndexOf("/", 7);
            }
            else
            {
                return string.Empty;
            }

            if (end < 0)
            {
                return string.Empty;
            }
            return url.Substring(end + 1);
        }

        static string GetFirstCharacters(string text, int characters)
        {
            if (characters < text.Length)
            {
                return text.Substring(0, characters) + "...";
            }
            else
            {
                return text.Substring(0, text.Length);
            }
        }

        static bool TryParseJToken(string json, out JToken jtoken)
        {
            try
            {
                jtoken = JToken.Parse(json);
                return true;
            }
            catch
            {
                jtoken = new JObject();
                return false;
            }
        }

        static bool TryParseJObject(string json, out JObject jobject)
        {
            try
            {
                jobject = JObject.Parse(json);
                return true;
            }
            catch
            {
                jobject = new JObject();
                return false;
            }
        }

        static bool TryParseJArray(string json, out JArray jarray)
        {
            try
            {
                JsonReader reader = new JsonTextReader(new StringReader(json));
                reader.DateParseHandling = DateParseHandling.None;
                jarray = JArray.Load(reader);

                //jarray = JArray.Parse(json);
                return true;
            }
            catch
            {
                jarray = new JArray();
                return false;
            }
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
        }

        static void Log(string message, ConsoleColor color)
        {
            var oldcolor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = oldcolor;
        }

        static void LogVerbose(string message)
        {
            if (verbose)
            {
                var oldcolor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(message);
                Console.ForegroundColor = oldcolor;
            }
        }
    }
}
