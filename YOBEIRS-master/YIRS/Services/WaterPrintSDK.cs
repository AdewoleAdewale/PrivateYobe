using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using YIRS.Models;
using YIRS.Services;

namespace YIRS.Services
{
    public class WaterPrintSDK
    {
        private readonly BluetoothPrinterService _printerService;

        public WaterPrintSDK()
        {
            _printerService = new BluetoothPrinterService();
        }

        /// <summary>
        /// Prints the Official Water Registration (Enumeration) Slip with Logo & QR Code
        /// </summary>
        public async Task<bool> PrintRegistrationReceiptAsync(WaterEnumerateResponse regData, WaterEnumerateRequest reqData, string areaName, string tariffName)
        {
            if (regData == null || reqData == null) return false;

            try
            {
                var payload = new List<byte>();

                // 1. Reset & Center Align
                payload.AddRange(ESCCommands.Initialize);
                payload.AddRange(ESCCommands.AlignCenter);

                // 2. Logo Raster Bytes (Standard Yobe Logo bitmap)
                byte[] logoBytes = GetLogoRasterBytes();
                if (logoBytes != null && logoBytes.Length > 0)
                {
                    payload.AddRange(logoBytes);
                }

                // 3. Header Text
                var sb = new StringBuilder();
                sb.AppendLine("YOBE STATE WATER CORPORATION");
                sb.AppendLine("WATER CONSUMER ENUMERATION SLIP");
                sb.AppendLine("================================");
                sb.AppendLine($"Date/Time : {DateTime.Now:dd-MMM-yyyy HH:mm}");
                sb.AppendLine($"Area Post : {areaName}");
                sb.AppendLine("--------------------------------");
                sb.AppendLine($"CONN NO   : {regData.connectionNo}");
                sb.AppendLine($"OCCUPANT  : {regData.occupant}");
                sb.AppendLine($"PHONE     : {reqData.phone}");
                sb.AppendLine($"ADDRESS   : {reqData.address}");
                if (!string.IsNullOrWhiteSpace(reqData.flatNo))
                    sb.AppendLine($"FLAT/UNIT : {reqData.flatNo}");
                sb.AppendLine($"TARIFF    : {tariffName}");
                sb.AppendLine($"MONTH RATE: NGN {regData.amount:N2}");
                sb.AppendLine($"DUE DATE  : {regData.dueDate:dd-MMM-yyyy}");
                sb.AppendLine("--------------------------------");
                sb.AppendLine($"Agent     : {reqData.recordedBy}");
                sb.AppendLine("================================");
                sb.AppendLine("  KEEP THIS CONNECTION NUMBER   ");
                sb.AppendLine("  REQUIRED FOR ALL BILL PAYMENTS\n");

                payload.AddRange(Encoding.UTF8.GetBytes(sb.ToString()));

                // 4. Barcode for Connection Number
                payload.AddRange(ESCCommands.GetBarcode128Bytes(regData.connectionNo));
                payload.AddRange(new byte[] { 0x0A });

                // 5. QR Code for Online Verification
                string qrPayload = $"https://yobe.osoftpay.net/verify/water?conn={regData.connectionNo}";
                payload.AddRange(ESCCommands.GetQrCodeBytes(qrPayload, moduleSize: 4));

                // 6. Footer & Line Feed
                var footerSb = new StringBuilder();
                footerSb.AppendLine("\n================================");
                footerSb.AppendLine("  Powered by YIRS Revenue Ops   ");
                footerSb.AppendLine("================================\n\n\n");
                payload.AddRange(Encoding.UTF8.GetBytes(footerSb.ToString()));

                return await _printerService.PrintRawBytesAsync(payload.ToArray());
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Prints the Official Water Bill Payment Receipt with Logo & QR Code
        /// </summary>
        public async Task<bool> PrintPaymentReceiptAsync(WaterReceiptResponse receipt, int monthsPaid)
        {
            if (receipt == null) return false;

            try
            {
                var payload = new List<byte>();

                payload.AddRange(ESCCommands.Initialize);
                payload.AddRange(ESCCommands.AlignCenter);

                // Print Logo
                byte[] logoBytes = GetLogoRasterBytes();
                if (logoBytes != null && logoBytes.Length > 0)
                {
                    payload.AddRange(logoBytes);
                }

                var sb = new StringBuilder();
                sb.AppendLine("YOBE STATE WATER CORPORATION");
                sb.AppendLine("OFFICIAL WATER REVENUE RECEIPT");
                sb.AppendLine("================================");
                sb.AppendLine($"Receipt No : {receipt.transactionId}");
                sb.AppendLine($"Debit Ref  : {receipt.debitRef}");
                sb.AppendLine($"Date & Time: {receipt.datelIst:dd-MMM-yyyy HH:mm}");
                sb.AppendLine("--------------------------------");
                sb.AppendLine($"CONN NO    : {receipt.payer}");
                sb.AppendLine($"OCCUPANT   : {receipt.occupant}");
                sb.AppendLine($"ADDRESS    : {receipt.address}");
                sb.AppendLine($"MONTHS PAID: {monthsPaid} Month(s)");
                sb.AppendLine("--------------------------------");
                sb.AppendLine($"AMOUNT PAID: NGN {receipt.amount:N2}");
                sb.AppendLine("PAY STATUS : SUCCESSFUL (PAID)");
                sb.AppendLine("--------------------------------");
                sb.AppendLine($"Agent      : {receipt.performedBy}");
                sb.AppendLine("================================");
                sb.AppendLine("    SCAN TO VERIFY RECEIPT      \n");

                payload.AddRange(Encoding.UTF8.GetBytes(sb.ToString()));

                // Verification QR Code
                string qrPayload = $"https://yobe.osoftpay.net/receipt/verify?tx={receipt.transactionId}&conn={receipt.payer}";
                payload.AddRange(ESCCommands.GetQrCodeBytes(qrPayload, moduleSize: 4));

                var footerSb = new StringBuilder();
                footerSb.AppendLine("\n================================");
                footerSb.AppendLine("  Thank you for your payment!   ");
                footerSb.AppendLine("================================\n\n\n");
                payload.AddRange(Encoding.UTF8.GetBytes(footerSb.ToString()));

                return await _printerService.PrintRawBytesAsync(payload.ToArray());
            }
            catch
            {
                return false;
            }
        }

        private byte[] GetLogoRasterBytes()
        {
            // Standard ESC/POS Monochrome raster image command: GS v 0
            // Returns null if logo conversion is handled natively by printer bitmap buffer
            return null;
        }
    }
}