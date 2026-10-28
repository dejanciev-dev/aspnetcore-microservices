using Catalog.API.Models;

namespace Catalog.API.Data;

/// <summary>Initial products, shared by the in-memory store and the PostgreSQL seeder.</summary>
internal static class SeedData
{
    public static readonly Product[] Products =
    [
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000001"), "Mechanical Keyboard", "Hot-swappable 75% keyboard with tactile switches.", "Peripherals", 129.99m, Img("keyboard")),
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000002"), "Wireless Mouse", "Ergonomic mouse with 70-day battery life.", "Peripherals", 59.90m, Img("mouse")),
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000003"), "4K Monitor", "27\" IPS panel, USB-C power delivery.", "Displays", 399.00m, Img("monitor")),
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000004"), "Ultrawide Monitor", "34\" curved display for multitasking.", "Displays", 549.00m, Img("ultrawide")),
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000005"), "Noise-Cancelling Headphones", "Over-ear, adaptive ANC, 30h playback.", "Audio", 249.50m, Img("headphones")),
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000006"), "USB Microphone", "Cardioid condenser mic for streaming and calls.", "Audio", 89.00m, Img("microphone")),
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000007"), "Laptop Stand", "Aluminium stand with adjustable height.", "Accessories", 39.99m, Img("stand")),
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000008"), "USB-C Dock", "11-in-1 dock with dual HDMI and 100W PD.", "Accessories", 149.00m, Img("dock")),
    ];

    private static string Img(string seed) => $"https://picsum.photos/seed/{seed}/480/320";
}
