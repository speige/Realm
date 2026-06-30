using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using System.Net;

namespace Realm.Lobby.Services;

public class GeoIpService
{
    private readonly DatabaseReader? _reader;

    public GeoIpService()
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GeoLite2-City.mmdb");
        if (File.Exists(dbPath))
        {
            try
            {
                _reader = new DatabaseReader(dbPath);
                Console.WriteLine("[GeoIP] Loaded GeoLite2 database.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GeoIP] Failed to load GeoLite2 database: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("[GeoIP] GeoLite2-City.mmdb not found. Falling back to simulation mode.");
        }
    }

    public (double lat, double lon) GetCoordinates(string ipAddress)
    {
        if (_reader != null && IPAddress.TryParse(ipAddress, out var ip) && !IPAddress.IsLoopback(ip))
        {
            try
            {
                var city = _reader.City(ip);
                if (city.Location.Latitude.HasValue && city.Location.Longitude.HasValue)
                {
                    return (city.Location.Latitude.Value, city.Location.Longitude.Value);
                }
            }
            catch (AddressNotFoundException) { /* Fallback */ }
            catch (Exception ex)
            {
                Console.WriteLine($"[GeoIP] Lookup error: {ex.Message}");
            }
        }


        if (ipAddress == "127.0.0.1" || ipAddress == "localhost")
        {

            return (38.9072, -77.0369);
        }


        string[] parts = ipAddress.Split('.');
        if (parts.Length > 0 && int.TryParse(parts[0], out int firstOctet))
        {
            int bucket = firstOctet % 4;
            return bucket switch
            {
                0 => (37.7749, -122.4194), // US West (San Francisco)
                1 => (40.7128, -74.0060),  // US East (New York)
                2 => (51.5074, -0.1278),   // Europe (London)
                3 => (35.6762, 139.6503),  // Asia (Tokyo)
                _ => (38.9072, -77.0369)
            };
        }

        return (38.9072, -77.0369);
    }

    public static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var R = 6371; // In kilometers
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double val) => (Math.PI / 180) * val;
}
