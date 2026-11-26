using DeckIsland.Core;

static class HudScrollTests
{
    public static void Run(Action<string, bool> check)
    {
        check("Notification scroll: a line that fits keeps the chosen time", HudScroll.Plan(0, 5, 10) == (0, 5));
        var (scroll, hold) = HudScroll.Plan(HudScroll.Speed * 2, 5, 10);
        check("Notification scroll: a short overflow scrolls whole within the chosen time", scroll == 2 && hold == 5);
        (scroll, hold) = HudScroll.Plan(HudScroll.Speed * 6, 5, 10);
        check("Notification scroll: a longer one stays until it has scrolled and been read",
            scroll == 6 && hold == HudScroll.Lead + 6 + HudScroll.Tail);
        (scroll, hold) = HudScroll.Plan(HudScroll.Speed * 60, 5, 10);
        check("Notification scroll: a very long one stops at the limit, still read at the end",
            hold == 10 && scroll == 10 - HudScroll.Lead - HudScroll.Tail);
        check("Notification scroll: a limit below the chosen time never shortens it", HudScroll.Plan(HudScroll.Speed * 60, 12, 5).Hold == 12);
        check("Device icons: a phone, earphones and anything else by class or name",
            HudDevices.ForBluetooth("iPhone", false, false) == HudIcon.Phone && HudDevices.ForBluetooth("Adrian's AirPods - Find My", false, false) == HudIcon.Headphones
            && HudDevices.ForBluetooth("Desk", false, true) == HudIcon.Headphones && HudDevices.ForBluetooth("MX Keys", false, false) == HudIcon.Bluetooth);
        check("Device icons: an output is headphones by its form factor or name, else speakers",
            HudDevices.ForOutput("Headphones (Adrian's AirPods - Find My)", null) == HudIcon.Headphones && HudDevices.ForOutput("Realtek", 3) == HudIcon.Headphones
            && HudDevices.ForOutput("Speakers (Realtek(R) Audio)", 1) == HudIcon.Speaker);
    }
}
