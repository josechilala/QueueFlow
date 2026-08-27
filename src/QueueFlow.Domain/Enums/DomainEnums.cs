namespace QueueFlow.Domain.Enums;

public enum QueueStatus { Draft, Open, Paused, Closed }
public enum TicketStatus { Waiting, Called, InService, Completed, Cancelled, NoShow, Transferred }
public enum TicketPriority { Normal = 0, Priority = 10 }
public enum UserRole { Owner, Admin, Manager, Attendant, Viewer }
public enum NotificationChannel { InApp, Email, WhatsApp, Sms, Push }
public enum NotificationStatus { Pending, Sent, Failed }
public enum SubscriptionStatus { Trial, Active, PastDue, Cancelled }
public enum ServiceAttendanceMode { QueueOnly = 1, AppointmentOnly = 2, Hybrid = 3 }
public enum AppointmentStatus { Scheduled = 1, Confirmed = 2, CheckedIn = 3, Completed = 4, Cancelled = 5, NoShow = 6, Rescheduled = 7 }
public enum ScheduleBlockType { Holiday = 1, Maintenance = 2, StaffUnavailable = 3, ManualBlock = 4, Other = 5 }
