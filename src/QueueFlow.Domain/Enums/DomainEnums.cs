namespace QueueFlow.Domain.Enums;

public enum QueueStatus { Draft, Open, Paused, Closed }
public enum TicketStatus { Waiting, Called, InService, Completed, Cancelled, NoShow, Transferred }
public enum TicketPriority { Normal = 0, Priority = 10 }
public enum UserRole { Owner, Admin, Manager, Attendant, Viewer }
public enum NotificationChannel { InApp, Email, WhatsApp, Sms, Push }
public enum NotificationStatus { Pending, Sent, Failed }
public enum SubscriptionStatus { Trial, Active, PastDue, Cancelled }
