using System;
using System.Collections.Generic;

namespace YIRS.Models
{
    public class WaterArea
    {
        public int id { get; set; }
        public string name { get; set; }
    }

    public class WaterAreaResponse
    {
        public string respondCode { get; set; }
        public string message { get; set; }
        public List<WaterArea> areas { get; set; }
    }

    public class WaterServiceTariff
    {
        public int id { get; set; }
        public string serviceName { get; set; }
        public decimal amount { get; set; }
    }

    public class WaterServicesResponse
    {
        public string respondCode { get; set; }
        public string message { get; set; }
        public List<WaterServiceTariff> services { get; set; }
    }

    public class WaterEnumerateRequest
    {
        public string occupant { get; set; }
        public string flatNo { get; set; }
        public string address { get; set; }
        public string lga { get; set; }
        public string location { get; set; }
        public string phone { get; set; }
        public string email { get; set; }
        public int areaId { get; set; }
        public int serviceId { get; set; }
        public string recordedBy { get; set; }
    }

    public class WaterEnumerateResponse
    {
        public string respondCode { get; set; }
        public string message { get; set; }
        public string connectionNo { get; set; }
        public string occupant { get; set; }
        public decimal amount { get; set; }
        public DateTime? dueDate { get; set; }
    }

    public class WaterConnectionStatusResponse
    {
        public string respondCode { get; set; }
        public string message { get; set; }
        public string connectionNo { get; set; }
        public string occupant { get; set; }
        public string address { get; set; }
        public string tarifPlan { get; set; }
        public decimal monthlyAmount { get; set; }
        public DateTime? dueDate { get; set; }
        public DateTime? lastPaymentDate { get; set; }
        public decimal? lastPaymentAmount { get; set; }
        public string status { get; set; }
        public int monthsOwedOrAhead { get; set; }
        public decimal backlogAmount { get; set; }
    }

    public class WaterPaymentRequest
    {
        public string email { get; set; }
        public string pin { get; set; }
        public string payer { get; set; }
        public string vehicleNo { get; set; } = "";
        public int monthsToPay { get; set; }
    }

    public class WaterPaymentResponse
    {
        public string respondCode { get; set; }
        public string transactionNo { get; set; }
        public string message { get; set; }
        public string payerName { get; set; }
        public decimal totalAmount { get; set; }
        public string serviceName { get; set; }
        public int monthsPaid { get; set; }
        public string connectionNo { get; set; }
    }

    public class WaterReceiptResponse
    {
        public string respondCode { get; set; }
        public string message { get; set; }
        public string transactionId { get; set; }
        public string payer { get; set; }
        public string occupant { get; set; }
        public string address { get; set; }
        public decimal amount { get; set; }
        public DateTime datelIst { get; set; }
        public string debitRef { get; set; }
        public string performedBy { get; set; }
    }


    public class WaterEnumerationItem
    {
        public string connectionNo { get; set; }
        public string occupant { get; set; }
        public string address { get; set; }
        public string areaOffice { get; set; }
        public string tarifPlan { get; set; }
        public decimal amount { get; set; }
        public string phone { get; set; }
        public string recordedBy { get; set; }
        public DateTime? dateRecorded { get; set; }
        public DateTime? dueDate { get; set; }
    }

    public class WaterEnumerationHistoryResponse
    {
        public string respondCode { get; set; }
        public string message { get; set; }
        public List<WaterEnumerationItem> connections { get; set; }
    }

    // --- Client Payment History Models ---
    public class WaterPaymentHistoryItem
    {
        public string transactionId { get; set; }
        public decimal amount { get; set; }
        public DateTime datelIst { get; set; }
        public string debitRef { get; set; }
        public string performedBy { get; set; }
    }

    public class WaterPaymentHistoryResponse
    {
        public string respondCode { get; set; }
        public string message { get; set; }
        public List<WaterPaymentHistoryItem> payments { get; set; }
    }
}