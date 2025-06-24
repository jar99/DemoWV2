using System.Runtime.InteropServices;

public class UefiSettings
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr GetFirmwareEnvironmentVariable(string lpName, string lpGuid, IntPtr pBuffer, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint GetLastError();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetFirmwareEnvironmentVariable(string lpName, string lpGuid, IntPtr pBuffer, uint nSize);


    public static void ListUefiVariables()
    {
        // This is a placeholder as reliably iterating all UEFI variables requires elevated privileges
        // and platform specific logic which goes beyond a simple example.
        // In real scenarios, consult motherboard vendor SDK or ACPI tables.

        Console.WriteLine("Listing Boot Options:");

        // Common GUID for Boot Services
        string vendorGuid = "{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}";

        // Iterate through possible Boot#### variables (e.g., Boot0000, Boot0001, etc.)
        for (int i = 0; i < 10; i++) // Check the first 10 possible boot options
        {
            string variableName = $"Boot{i:D4}"; // Format as Boot0000, Boot0001, etc.

            byte[] buffer = new byte[1024]; // Increased buffer size for potentially larger boot options
            IntPtr bufferPtr = Marshal.AllocHGlobal(buffer.Length);

            IntPtr result = GetFirmwareEnvironmentVariable(variableName, vendorGuid, bufferPtr, (uint)buffer.Length);

            if (result != IntPtr.Zero)
            {
                int size = (int)result;
                byte[] data = new byte[size];
                Marshal.Copy(bufferPtr, data, 0, size);

                Console.WriteLine($"Variable '{variableName}' found. Size: {size}");
                Console.Write($"Description: ");
                try
                {
                    //Attempt to decode as UTF-16
                    string description = System.Text.Encoding.Unicode.GetString(data);
                    Console.WriteLine(description);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Could not decode as UTF-16: {e.Message}");
                    Console.WriteLine($"Value (Hex): {BitConverter.ToString(data)}");
                }
            }
            else
            {
                uint error = GetLastError();
                //It's ok to get an error if the variable doesn't exist, so only print other errors.
                if (error != 2) // 2 = ERROR_FILE_NOT_FOUND, meaning the variable wasn't set.
                {
                    Console.WriteLine($"Error reading variable '{variableName}': {error}");
                }
            }

            Marshal.FreeHGlobal(bufferPtr);
        }
    }
}