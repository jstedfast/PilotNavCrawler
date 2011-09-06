// 
// PilotNavCrawler.cs
//  
// Author: Jeffrey Stedfast <jeff@xamarin.com>
// 
// Copyright (c) 2011 Jeffrey Stedfast
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
// 

using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Collections;
using System.Collections.Generic;

using HtmlAgilityPack;
using SQLite;

namespace PilotNavCrawler
{
	public class PilotNavCrawler
	{
		static string PilotNavWebSite = "http://www.pilotnav.com";
		static string BrowseContinents = "http://www.pilotnav.com/browse/Airports";
		static string ContinentFormat = BrowseContinents + "/continent/{0}";
		static string CountryFormat = ContinentFormat + "/country/{1}";
		static string StateFormat = CountryFormat + "/state/{2}";
		static string USAPageFormat = StateFormat + "/p/{3}";
		static string PageFormat = CountryFormat + "/p/{2}";
		
		static string PilotNavAirportFormat = "http://www.pilotnav.com/airport/{0}";
		static string AirportPath = "/airport/";
		
		Queue<string> continents = new Queue<string> ();
		Queue<string> countries = new Queue<string> ();
		Queue<string> airports = new Queue<string> ();
		Queue<string> states = new Queue<string> ();
		Queue<string> pages = new Queue<string> ();
		string continent, country, state;
		SQLiteConnection sqlitedb;
		string page, airport;
		
		static string EncodeContinent (string continent)
		{
			string[] words = continent.Split (new char[] { ' ' });
			for (int i = 0; i < words.Length; i++)
				words[i] = char.ToUpperInvariant (words[i][0]).ToString () + words[i].Substring (1).ToLowerInvariant ();
			
			return string.Join ("%20", words);
		}
		
		static string EncodeCountry (string country)
		{
			string[] words = country.Split (new char[] { ' ' });
			for (int i = 0; i < words.Length; i++)
				words[i] = words[i].ToUpperInvariant ();
			
			return string.Join ("%20", words);
		}
		
		static string EncodeState (string state)
		{
			string[] words = state.Split (new char[] { ' ' });
			for (int i = 0; i < words.Length; i++)
				words[i] = words[i].ToUpperInvariant ();
			
			return string.Join ("%20", words);
		}
		
		private PilotNavCrawler () { }
		
		public static PilotNavCrawler Create (string filename, string continent, string country, string state)
		{
			if (filename == null)
				throw new ArgumentNullException ("filename");
			
			PilotNavCrawler crawler = new PilotNavCrawler ();
			crawler.sqlitedb = new SQLiteConnection (filename);
			crawler.sqlitedb.CreateTable<Airport> ();
			
			if (continent != null) {
				crawler.continents.Enqueue (EncodeContinent (continent));
				
				if (country != null) {
					crawler.countries.Enqueue (EncodeCountry (country));
					
					if (state != null)
						crawler.states.Enqueue (EncodeState (state));
				}
			}
			
			return crawler;
		}
		
		public static PilotNavCrawler Create (string filename, string continent, string country)
		{
			return Create (filename, continent, country, null);
		}
		
		public static PilotNavCrawler Create (string filename, string continent)
		{
			return Create (filename, continent, null);
		}
		
		public static PilotNavCrawler Create (string filename)
		{
			return Create (filename, null);
		}
		
		List<string> GetChildren (Stream stream, string hrefBase)
		{
			List<string> children = hrefBase != null ? new List<string> () : null;
			HtmlDocument doc = new HtmlDocument ();
			
			doc.Load (stream);
			
			foreach (HtmlNode link in doc.DocumentNode.SelectNodes ("//a[@href]")) {
				HtmlAttribute href = link.Attributes["href"];
				
				if (href == null || href.Value == null)
					continue;
				
				if (hrefBase != null && href.Value.StartsWith (hrefBase) && href.Value.Length > hrefBase.Length)
					children.Add (href.Value.Substring (hrefBase.Length));
				
				if (href.Value.StartsWith (AirportPath) && href.Value.Length > AirportPath.Length) {
					string code = href.Value.Substring (AirportPath.Length);
					
					if (!airports.Contains (code))
						airports.Enqueue (code);
				}
			}
			
			return children;
		}
		
		List<string> GetChildren (string requestUri, string hrefBase)
		{
			HttpWebResponse response;
			HttpWebRequest request;
			
			//Console.WriteLine ("Requesting URL: {0}", requestUri);
			request = (HttpWebRequest) WebRequest.Create (requestUri);
			request.AllowAutoRedirect = true;
			
			try {
				response = (HttpWebResponse) request.GetResponse ();
				return GetChildren (response.GetResponseStream (), hrefBase);
			} catch (Exception ex) {
				Console.Error.WriteLine ("Failed to fetch {0}: {1}\n{2}", requestUri, ex.Message, ex.StackTrace);
				return new List<string> ();
			}
		}
		
		void QueueContinents ()
		{
			string hrefBase = string.Format (ContinentFormat, "");
			
			foreach (var child in GetChildren (BrowseContinents, hrefBase.Substring (PilotNavWebSite.Length)))
				continents.Enqueue (child);
			
			Thread.Sleep (1000);
		}
		
		void QueueCountries ()
		{
			string requestUri = string.Format (ContinentFormat, continent);
			string hrefBase = string.Format (CountryFormat, continent, "");
			
			foreach (var child in GetChildren (requestUri, hrefBase.Substring (PilotNavWebSite.Length)))
				countries.Enqueue (child);
			
			Thread.Sleep (1000);
		}
		
		void QueueStates ()
		{
			string requestUri = string.Format (CountryFormat, continent, country);
			string hrefBase = string.Format (StateFormat, continent, country, "");
			
			foreach (var child in GetChildren (requestUri, hrefBase.Substring (PilotNavWebSite.Length)))
				states.Enqueue (child);
			
			Thread.Sleep (1000);
		}
		
		void QueuePages (bool IsUSA)
		{
			string requestUri;
			string hrefBase;
			
			if (IsUSA) {
				requestUri = string.Format (StateFormat, continent, country, state);
				hrefBase = string.Format (USAPageFormat, continent, country, state, "");
			} else {
				requestUri = string.Format (CountryFormat, continent, country);
				hrefBase = string.Format (PageFormat, continent, country, "");
			}
			
			foreach (var child in GetChildren (requestUri, hrefBase.Substring (PilotNavWebSite.Length)))
				pages.Enqueue (child);
			
			Thread.Sleep (1000);
		}
		
		void ScrapePage (bool IsUSA)
		{
			string requestUri;
			
			if (IsUSA)
				requestUri = string.Format (USAPageFormat, continent, country, state, page);
			else
				requestUri = string.Format (PageFormat, continent, country, page);
			
			GetChildren (requestUri, null);
		}
		
		static string airport_code_div_class = "code_box code_";
		static Dictionary<string, string> GetAirportCodes (HtmlDocument doc)
		{
			Dictionary<string, string> values = new Dictionary<string, string> ();
			
			foreach (HtmlNode div in doc.DocumentNode.SelectNodes ("//div")) {
				HtmlAttribute attr = div.Attributes["class"];
				
				if (attr == null)
					continue;
				
				string @class = attr.Value;
				
				if (@class.StartsWith (airport_code_div_class)) {
					int start = airport_code_div_class.Length;
					int end = @class.LastIndexOf ('_');
					string key = @class.Substring (start, end - start);
					
					// the code is the text content
					values.Add (key.ToUpperInvariant (), div.InnerText.Trim ());
					
					if (key == "faa") {
						// FAA code is always the last
						break;
					}
				}
			}
			
			return values.Count > 0 ? values : null;
		}
		
		static string GetAirportName (HtmlDocument doc)
		{
			foreach (HtmlNode h1 in doc.DocumentNode.SelectNodes ("//table/tr/td/h1")) {
				if (h1.InnerText == null)
					continue;
				
				return h1.InnerText.Trim ();
			}
			
			return null;
		}
		
		static string[] GetAirportLocation (HtmlDocument doc, bool IsUSA)
		{
			foreach (HtmlNode h2 in doc.DocumentNode.SelectNodes ("//table/tr/td/h2")) {
				if (h2.InnerText == null)
					continue;
				
				// The text content should be of the form: City, State, Country
				string[] location = h2.InnerText.Trim ().Split (new char[] { ',' });
				string country, state, city;
				int n = 1;
				
				for (int i = 0; i < location.Length; i++)
					location[i] = location[i].Trim ();
				
				switch (location.Length) {
				case 3: /* City, State, Country */
					if (!IsUSA) {
						// No State component for non-US countries.
						goto default;
					}
					
					/* We're golden. */
					return location;
				case 2: /* City, Country */
					country = location[1];
					city = location[0];
					state = null;
					break;
				case 1: /* Country */
					country = location[0];
					state = null;
					city = null;
					break;
				default: // City name must have commas in its name.
					if (IsUSA) {
						// If in the USA, the second-to-last component must be the State.
						n++;
					}
					
					city = string.Join (", ", location, 0, location.Length - n);
					country = location[location.Length - 1];
					
					if (IsUSA)
						state = location[location.Length - 2];
					else
						state = null;
					
					break;
				}
				
				return new string[] { city, state, country };
			}
			
			return null;
		}
		
		static Dictionary<string, string> GetAirportKeyValues (HtmlDocument doc)
		{
			Dictionary<string, string> data = new Dictionary<string, string> ();
			HtmlNode next;
			string key;
			
			foreach (HtmlNode td in doc.DocumentNode.SelectNodes ("//td[@class]")) {
				if (!td.HasAttributes)
					continue;
				
				HtmlAttribute @class = td.Attributes["class"];
				
				if (@class == null || @class.Value != "dataLabel")
					continue;
				
				// the key is the text content of this <td class="dataLabel"> element
				key = td.InnerText.Trim ();
				if (!key.EndsWith (":"))
					continue;
				
				// get rid of the trailing ':'
				key = key.Substring (0, key.Length - 1);
				
				// the value is the content of the next <td> element
				next = td.NextSibling;
				while (next != null && next.NodeType != HtmlNodeType.Element)
					next = next.NextSibling;
				
				if (next.Name != "td")
					continue;
				
				if (!data.ContainsKey (key))
					data.Add (key, next.InnerText.Trim ());
			}
			
			return data.Count > 0 ? data : null;
		}
		
		static Airport ParseAirport (Stream stream, bool IsUSA)
		{
			HtmlDocument doc = new HtmlDocument ();
			Dictionary<string, string> values;
			Airport airport = new Airport ();
			string[] elevation;
			string[] location;
			string value;
			double d;
			int i;
			
			doc.Load (stream);
			
			if ((values = GetAirportCodes (doc)) == null)
				throw new Exception ("Could not find airport codes.");
			
			if (values.TryGetValue ("ICAO", out value))
				airport.ICAO = value;
			if (values.TryGetValue ("IATA", out value))
				airport.IATA = value;
			if (values.TryGetValue ("FAA", out value))
				airport.FAA = value;
			
			if ((airport.Name = GetAirportName (doc)) == null)
				throw new Exception ("Failed to scrape airport name.");
			
			if ((location = GetAirportLocation (doc, IsUSA)) == null)
				throw new Exception ("Failed to scrape airport location.");
			
			airport.City = location[0];
			airport.State = location[1];
			airport.Country = location[2];
			
			if ((values = GetAirportKeyValues (doc)) == null)
				throw new Exception ("Could not find key/value information.");
			
			if (!values.TryGetValue ("Latitude", out value))
				throw new Exception ("Airport key values did not contain the Latitude coordinate.");
			
			if (!double.TryParse (value, out d))
				throw new Exception (string.Format ("Could not parse Latitude: {0}", value));
			
			airport.Latitude = d;
			
			if (!values.TryGetValue ("Longitude", out value))
				throw new Exception ("Airport key values did not contain the Longitude coordinate.");
			
			if (!double.TryParse (value, out d))
				throw new Exception (string.Format ("Could not parse Longitude: {0}", value));
			
			airport.Longitude = d;
			
			if (values.TryGetValue ("Elevation", out value)) {
				elevation = value.Split (new char[] { ' ' });
				if (!int.TryParse (elevation[0], out i)) {
					Console.Error.WriteLine ("Could not parse Elevation data for {0}: '{1}'", airport.FAA, value);
				} else {
					airport.Elevation = i;
				}
			} else {
				// Not critical if we can't get elevation, although it would be nice to have...
				Console.Error.WriteLine ("Airport {0} did not contain Elevation data.", airport.FAA);
			}
			
			return airport;
		}
		
		void ScrapeAirport (bool IsUSA)
		{
			string requestUri = string.Format (PilotNavAirportFormat, airport);
			HttpWebResponse response;
			HttpWebRequest request;
			Airport record = null;
			
			//Console.WriteLine ("Requesting URL: {0}", requestUri);
			request = (HttpWebRequest) WebRequest.Create (requestUri);
			request.AllowAutoRedirect = true;
			
			try {
				response = (HttpWebResponse) request.GetResponse ();
			} catch (Exception ex) {
				Console.Error.WriteLine ("Failed to fetch airport: {0}: {1}\n{2}", requestUri, ex.Message, ex.StackTrace);
				return;
			}
			
			try {
				//Console.WriteLine ("Parsing airport {0}...", airport);
				record = ParseAirport (response.GetResponseStream (), IsUSA);
			} catch (Exception ex) {
				Console.Error.WriteLine ("Failed to parse airport information from {0}", requestUri);
				Console.Error.WriteLine (ex);
				return;
			}
			
			try {
				Console.WriteLine ("Filing airport, {0}, under {1}, {2}, {3}", record.FAA, record.City, record.State, record.Country);
				sqlitedb.Insert (record);
			} catch (Exception ex) {
				Console.Error.WriteLine ("Failed to add airport to database. URL was {0}", requestUri);
				Console.Error.WriteLine (ex);
				
				var results = sqlitedb.Query<Airport> ("select 1 from Airport where FAA = ?", record.FAA);
				if (results.Count > 0)
					Console.Error.WriteLine ("Looks like the airport for FAA={0} already exists.", record.FAA);
				
				return;
			}
			
			Thread.Sleep (1000);
		}
		
		public void Crawl ()
		{
			if (continents.Count == 0)
				QueueContinents ();
			
			while (continents.Count > 0) {
				continent = continents.Dequeue ();
				if (countries.Count == 0)
					QueueCountries ();
				
				while (countries.Count > 0) {
					country = countries.Dequeue ();
					
					if (country == "UNITED%20STATES") {
						if (states.Count == 0)
							QueueStates ();
						
						while (states.Count > 0) {
							state = states.Dequeue ();
							if (pages.Count == 0)
								QueuePages (true);
							
							while (pages.Count > 0) {
								page = pages.Dequeue ();
								ScrapePage (true);
							}
							
							while (airports.Count > 0) {
								airport = airports.Dequeue ();
								ScrapeAirport (true);
							}
						}
					} else {
						if (pages.Count == 0)
							QueuePages (false);
						
						while (pages.Count > 0) {
							page = pages.Dequeue ();
							ScrapePage (false);
						}
						
						while (airports.Count > 0) {
							airport = airports.Dequeue ();
							ScrapeAirport (false);
						}
					}
				}
			}
		}
	}
}
