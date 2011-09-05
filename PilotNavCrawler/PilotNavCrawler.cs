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
using System.Xml;
using System.Threading;
using System.Collections;
using System.Collections.Generic;

using SQLite;

namespace PilotNavCrawler
{
	public class PilotNavCrawler
	{
		static string PilotNavBrowseContinents = "http://www.pilotnav.com/browse/Airports";
		static string ContinentFormat = PilotNavBrowseContinents + "/continent/{0}";
		static string CountryFormat = ContinentFormat + "/country/{1}";
		static string StateFormat = CountryFormat + "/state/{2}";
		static string PageFormat = StateFormat + "/p/{3}";
		
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
				Console.WriteLine ("Create() queueing continent: {0}", continent);
				crawler.continents.Enqueue (EncodeContinent (continent));
				
				if (country != null) {
					Console.WriteLine ("Create() queueing country: {0}", country);
					crawler.countries.Enqueue (EncodeCountry (country));
					
					if (state != null) {
						Console.WriteLine ("Create() queueing state: {0}", state);
						crawler.states.Enqueue (EncodeState (state));
					}
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
			XmlReader reader = new XmlTextReader (stream);
			
			while (reader.Read ()) {
				if (reader.NodeType == XmlNodeType.Element && reader.Name == "a") {
					string href = reader.GetAttribute ("href");
					
					if (hrefBase != null && href.StartsWith (hrefBase) && href.Length > hrefBase.Length)
						children.Add (href.Substring (hrefBase.Length));
					
					if (href.StartsWith (AirportPath)) {
						string code = href.Substring (AirportPath.Length);
						if (!airports.Contains (code)) {
							Console.WriteLine ("Queueing airport: {0}", code);
							airports.Enqueue (code);
						}
					}
				}
			}
			
			return children;
		}
		
		List<string> GetChildren (string requestUri, string hrefBase)
		{
			HttpWebResponse response;
			HttpWebRequest request;
			
			request = (HttpWebRequest) WebRequest.Create (requestUri);
			request.AllowAutoRedirect = true;
			
			try {
				response = (HttpWebResponse) request.GetResponse ();
				return GetChildren (response.GetResponseStream (), hrefBase);
			} catch (Exception ex) {
				Console.Error.WriteLine ("Failed to fetch {0}: {1}", requestUri, ex.Message);
				return new List<string> ();
			}
		}
		
		void QueueContinents ()
		{
			string hrefBase = string.Format (ContinentFormat, "");
			
			foreach (var child in GetChildren (PilotNavBrowseContinents, hrefBase)) {
				Console.WriteLine ("Queueing continent: {0}", child);
				continents.Enqueue (child);
			}
			
			Thread.Sleep (1000);
		}
		
		void QueueCountries ()
		{
			string requestUri = string.Format (ContinentFormat, continent);
			string hrefBase = string.Format (CountryFormat, continent, "");
			
			foreach (var child in GetChildren (requestUri, hrefBase)) {
				Console.WriteLine ("Queueing country: {0}", child);
				countries.Enqueue (child);
			}
			
			Thread.Sleep (1000);
		}
		
		void QueueStates ()
		{
			string requestUri = string.Format (CountryFormat, continent, country);
			string hrefBase = string.Format (StateFormat, continent, country, "");
			
			foreach (var child in GetChildren (requestUri, hrefBase)) {
				Console.WriteLine ("Queueing state: {0}", child);
				states.Enqueue (child);
			}
			
			Thread.Sleep (1000);
		}
		
		void QueuePages ()
		{
			string requestUri = string.Format (StateFormat, continent, country, state);
			string hrefBase = string.Format (PageFormat, continent, country, state, "");
			
			foreach (var child in GetChildren (requestUri, hrefBase)) {
				Console.WriteLine ("Queueing page: {0}", child);
				pages.Enqueue (child);
			}
			
			Thread.Sleep (1000);
		}
		
		void ScrapePage ()
		{
			string requestUri = string.Format (PageFormat, continent, country, state, page);
			
			GetChildren (requestUri, null);
		}
		
		static bool ScanToElement (XmlTextReader reader, string name, string attr, string value, bool startsWith)
		{
			string v;
			
			while (reader.Read ()) {
				if (reader.NodeType == XmlNodeType.Element && reader.Name == name) {
					if (attr == null)
						return true;
					
					if (!reader.HasAttributes)
						continue;
					
					if ((v = reader.GetAttribute (attr)) == null)
						continue;
					
					if (value == null)
						return true;
					
					if (startsWith) {
						if (v.StartsWith (value))
							return true;
					} else if (v == value) {
						return true;
					}
				}
			}
			
			return false;
		}
		
		static string airport_code_div_class = "code_box code_";
		static Dictionary<string, string> GetAirportCodes (XmlTextReader reader)
		{
			string @class;
			
			// First, scan until we find a <div class=...>
			if (!ScanToElement (reader, "div", "class", airport_code_div_class, true))
				return null;
			
			Dictionary<string, string> values = new Dictionary<string, string> ();
			
			do {
				if (reader.NodeType != XmlNodeType.Element)
					continue;
				
				if (reader.Name != "div")
					break;
				
				if ((@class = reader.GetAttribute ("class")) == null)
					break;
				
				if (!@class.StartsWith (airport_code_div_class))
					break;
				
				// Okay, now we know it's an airport code...
				if (reader.HasValue) {
					string key = @class.Substring (airport_code_div_class.Length);
					int uscore = key.IndexOf ('_');
					key = key.Substring (0, uscore);
					
					values.Add (key.ToUpperInvariant (), reader.Value.Trim ());
				}
			} while (reader.Read ());
			
			return values;
		}
		
		static string GetAirportName (XmlTextReader reader)
		{
			if (!ScanToElement (reader, "h1", null, null, false))
				return null;
			
			if (!reader.HasValue)
				return null;
			
			return reader.Value.Trim ();
		}
		
		static string[] GetAirportLocation (XmlTextReader reader)
		{
			if (!ScanToElement (reader, "h2", null, null, false))
				return null;
			
			if (!reader.HasValue)
				return null;
			
			// The value should be of the form: City, State, Country
			string[] location = reader.Value.Trim ().Split (new char[] { ',' });
			
			if (location.Length == 3)
				return location;
			
			// Didn't get the expected number of tokens...
			
			if (location.Length < 3) {
				// Non-US location: first string is city, second is country.
				return new string[] { location[0], null, location[1] };
			}
			
			// Combine all but the last 2 strings into the city name
			string city = string.Join (", ", location, 0, location.Length - 2);
			string country = location[location.Length - 1];
			string state = location[location.Length - 2];
			
			return new string[] { city, state, country };
		}
		
		static Dictionary<string, string> GetAirportKeyValues (XmlTextReader reader)
		{
			if (!ScanToElement (reader, "td", "class", "dataLabel", false))
				return null;
			
			Dictionary<string, string> data = new Dictionary<string, string> ();
			string key;
			
			do {
				if (!reader.HasValue)
					continue;
				
				key = reader.Value.Trim ();
				if (!key.EndsWith (":"))
					continue;
				
				// Get rid of the trailing ':'
				key = key.Substring (0, key.Length - 1);
				
				// Scan ahead to the next td which contains the value
				if (!reader.Read () || !ScanToElement (reader, "td", null, null, false))
					break;
				
				if (!reader.HasValue)
					continue;
				
				data.Add (key, reader.Value.Trim ());
			} while (ScanToElement (reader, "td", "class", "dataLabel", false));
			
			return data;
		}
		
		static Airport ParseAirport (Stream stream)
		{
			XmlTextReader reader = new XmlTextReader (stream);
			Dictionary<string, string> values;
			Airport airport = new Airport ();
			string[] elevation;
			string[] location;
			string value;
			double d;
			int i;
			
			if ((values = GetAirportCodes (reader)) == null)
				throw new Exception ("Could not find airport codes.");
			
			if (values.TryGetValue ("ICAO", out value))
				airport.ICAO = value;
			if (values.TryGetValue ("IATA", out value))
				airport.IATA = value;
			if (values.TryGetValue ("FAA", out value))
				airport.FAA = value;
			
			if (!ScanToElement (reader, "table", null, null, false))
				throw new Exception ("Could not find table element containing the airport Name and Location values.");
			
			if ((airport.Name = GetAirportName (reader)) == null)
				throw new Exception ("Failed to scrape airport name.");
			
			if ((location = GetAirportLocation (reader)) == null)
				throw new Exception ("Failed to scrape airport location.");
			
			airport.City = location[0];
			airport.State = location[1];
			airport.Country = location[2];
			
			if ((values = GetAirportKeyValues (reader)) == null)
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
			
			if (!values.TryGetValue ("Elevation", out value))
				throw new Exception ("Airport key values did not contain the Elevation.");
			
			elevation = value.Split (new char[] { ' ' });
			if (!int.TryParse (elevation[0], out i))
				throw new Exception (string.Format ("Could not parse Elevation: {0}", value));
			
			airport.Elevation = i;
			
			return airport;
		}
		
		void ScrapeAirport ()
		{
			string requestUri = string.Format (PilotNavAirportFormat, airport);
			HttpWebResponse response;
			HttpWebRequest request;
			
			request = (HttpWebRequest) WebRequest.Create (requestUri);
			request.AllowAutoRedirect = true;
			
			try {
				response = (HttpWebResponse) request.GetResponse ();
				sqlitedb.Insert (ParseAirport (response.GetResponseStream ()));
			} catch (Exception ex) {
				Console.Error.WriteLine ("Failed to fetch airport: {0}: {1}", requestUri, ex.Message);
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
					if (states.Count == 0)
						QueueStates ();
					
					while (states.Count > 0) {
						state = states.Dequeue ();
						if (pages.Count == 0)
							QueuePages ();
						
						while (pages.Count > 0) {
							page = pages.Dequeue ();
							ScrapePage ();
						}
						
						while (airports.Count > 0) {
							airport = airports.Dequeue ();
							ScrapeAirport ();
						}
					}
				}
			}
		}
	}
}
