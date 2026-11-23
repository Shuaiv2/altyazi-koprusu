// SPDX-License-Identifier: AGPL-3.0-only

using GameTranslator.Core;

namespace GameTranslator.Core.Tests;

/// <summary>
/// mac/tests/ScreenTextTests.swift'in karşılığı. Fikstürler gerçek GTA 6 tam kare OCR'ından elle seçilmiş oyun metni satırlarıdır
/// (kare numarası yorumda); PS sistem bindirmesi ya da kullanıcı adı İÇERMEZ, PS bildirimi satırı sentetiktir.
/// </summary>
public class ScreenTextTests
{
    /// <summary>(x0, y0, x1, y1, güven, metin) — sol-üst başlangıç.</summary>
    internal readonly record struct Row(double X0, double Y0, double X1, double Y1, float Conf, string Text);

    internal static Row R(double x0, double y0, double x1, double y1, float c, string t) => new(x0, y0, x1, y1, c, t);

    internal static List<ScreenLine> Lines(params IEnumerable<Row>[] groups) =>
        groups.SelectMany(g => g).Select(r => new ScreenLine(r.Text, new ScreenBox(r.X0, r.Y0, r.X1 - r.X0, r.Y1 - r.Y0), r.Conf)).ToList();

    internal static Row[] One(Row r) => [r];

    private static readonly Row PsToast = R(0.411, 0.047, 0.589, 0.075, 1.0f, "A Uzaktan Oynama baglandi.");
    private static readonly Row[] Hud = [R(0.924, 0.026, 0.962, 0.054, 1.0f, "$500"), R(0.911, 0.059, 0.962, 0.090, 1.0f, "$2,441")];
    private static readonly Row[] MapPrompts = [R(0.905, 0.757, 0.961, 0.781, 1.0f, "RECENTER"), R(0.891, 0.852, 0.961, 0.879, 1.0f, "ADD MARKER"), R(0.894, 0.951, 0.959, 0.974, 1.0f, "CLOSE MENU")];

    private static ScreenAnalysis A(params Row[] rows) => ScreenText.Analyze(Lines(rows));

    private static string[] Texts(ScreenAnalysis a) => a.Blocks.Select(b => b.Text).ToArray();

    // --- Harita / menü ---

    [Fact]
    public void Frame0054_MapDescriptionWithTitle()
    {
        var f = Lines([PsToast,
            R(0.016, 0.038, 0.073, 0.076, 1, "STORY"), R(0.081, 0.121, 0.156, 0.145, 1, "Earn Money"),
            R(0.080, 0.181, 0.224, 0.209, 1, "Find Donnie's Package"), R(0.080, 0.240, 0.218, 0.269, 1, "Set Up Bank Account"),
            R(0.478, 0.500, 0.523, 0.531, 1, "0.40 mi"),
            R(0.576, 0.434, 0.722, 0.465, 1, "SET UP BANK ACCOUNT"),
            R(0.547, 0.483, 0.797, 0.509, 1, "Lucia needs a bank account to get her"),
            R(0.547, 0.519, 0.801, 0.545, 1, "community service payments. Meet her"),
            R(0.547, 0.556, 0.683, 0.581, 1, "at the Fleeca nearby."),
            R(0.905, 0.757, 0.961, 0.781, 1, "RECENTER"), R(0.917, 0.806, 0.959, 0.830, 1, "2D VIEW"),
            R(0.891, 0.852, 0.961, 0.879, 1, "ADD MARKER"), R(0.871, 0.902, 0.959, 0.925, 1, "SET DESTINATION"),
            R(0.894, 0.951, 0.959, 0.974, 1, "CLOSE MENU")], Hud);
        var r = ScreenText.Analyze(f);
        Assert.Equal(ScreenMode.Map, r.Mode);
        var b = Assert.Single(r.Blocks);
        Assert.Equal(ScreenKind.Description, b.Kind);
        Assert.Equal("Lucia needs a bank account to get her community service payments. Meet her at the Fleeca nearby.", b.Text);
        Assert.Equal("SET UP BANK ACCOUNT", b.Title);
    }

    [Fact]
    public void Frame0058_TwoLineDescription()
    {
        var r = A(PsToast,
            R(0.064, 0.119, 0.151, 0.145, 1, "- GAS STATIONS"), R(0.080, 0.181, 0.209, 0.209, 1, "247 Gas - Key Lento"),
            R(0.081, 0.240, 0.206, 0.261, 1, "Arrow Gas - Hamlet"), R(0.081, 0.300, 0.228, 0.323, 1, "Globe Oil Gas - Hamlet"),
            R(0.080, 0.419, 0.198, 0.442, 1, "Xero Gas - Hamlet"),
            R(0.545, 0.449, 0.682, 0.481, 1, "247 GAS - KEY LENTO"),
            R(0.547, 0.496, 0.781, 0.525, 1, "Fill up on fuel or quickly service your"), R(0.546, 0.532, 0.597, 0.559, 1, "vehicle."),
            R(0.905, 0.757, 0.961, 0.781, 1, "RECENTER"), R(0.891, 0.852, 0.961, 0.879, 1, "ADD MARKER"), R(0.894, 0.951, 0.959, 0.974, 1, "CLOSE MENU"));
        Assert.Equal(["Fill up on fuel or quickly service your vehicle."], Texts(r));
        Assert.Equal("247 GAS - KEY LENTO", r.Blocks[0].Title);
    }

    [Fact]
    public void Frame0117_IconStrippedTitleBodySegments()
    {
        var r = A([PsToast, R(0.576, 0.341, 0.693, 0.370, 1, "DONNIE'S TRAILER"), R(0.551, 0.395, 0.676, 0.424, 1, "$ No goods to sell"),
            R(0.547, 0.439, 0.762, 0.465, 1, "Donnie will pay for any narcotics,"), R(0.544, 0.475, 0.792, 0.504, 1, "jewelry and marked bills dropped off"),
            R(0.546, 0.511, 0.629, 0.538, 1, "at his trailer."), .. MapPrompts]);
        Assert.Equal("No goods to sell\nDonnie will pay for any narcotics, jewelry and marked bills dropped off at his trailer.", r.Blocks[0].Text);
    }

    [Fact]
    public void Frame0122_Segments()
    {
        var r = A([PsToast, R(0.576, 0.341, 0.718, 0.372, 1, "SAVE FOR THE FUTURE"), R(0.567, 0.398, 0.769, 0.426, 1, "Family Savings will earn interest"),
            R(0.547, 0.439, 0.770, 0.468, 1, "You are Buckin' Broke. Your family"), R(0.546, 0.471, 0.766, 0.507, 1, "needs savings for a better future."),
            R(0.547, 0.512, 0.767, 0.540, 1, "Contribute to your BuckMe Family"),
            R(0.546, 0.541, 0.783, 0.578, 1, "Savings account and reach the first"), R(0.546, 0.581, 0.640, 0.607, 1, "milestone."), .. MapPrompts]);
        Assert.StartsWith("Family Savings will earn interest\nYou are Buckin' Broke.", r.Blocks[0].Text);
    }

    [Fact]
    public void PauseMenuSettingDescription_IsMenuMode()
    {
        var r = A(PsToast, R(0.060, 0.180, 0.140, 0.208, 1, "AIMING"), R(0.060, 0.250, 0.180, 0.278, 1, "Camera Sensitivity"),
            R(0.520, 0.300, 0.700, 0.330, 1, "CAMERA SENSITIVITY"),
            R(0.520, 0.345, 0.830, 0.372, 1, "Adjusts how fast the camera turns while"), R(0.520, 0.380, 0.780, 0.407, 1, "aiming a weapon on foot."),
            R(0.863, 0.951, 0.903, 0.974, 1, "SELECT"), R(0.930, 0.951, 0.959, 0.974, 1, "BACK"));
        Assert.Equal(ScreenMode.Menu, r.Mode);
        var b = Assert.Single(r.Blocks);
        Assert.Equal("Adjusts how fast the camera turns while aiming a weapon on foot.", b.Text);
        Assert.Equal("CAMERA SENSITIVITY", b.Title);
    }

    [Fact]
    public void Frame0183_MenuListTranslatesNothing()
    {
        var r = A(R(0.910, 0.126, 0.981, 0.147, 1, "CHAPTER 1"), R(0.026, 0.287, 0.086, 0.310, 1, "Im) Game"), R(0.032, 0.352, 0.082, 0.375, 1, "• Store"),
            R(0.045, 0.421, 0.076, 0.447, 1, "Help"), R(0.025, 0.486, 0.129, 0.509, 1, "[ Photo Studio"), R(0.023, 0.550, 0.100, 0.581, 1, "ii Settings"),
            R(0.760, 0.951, 0.836, 0.974, 1, "PHOTO STUDIO"), R(0.863, 0.951, 0.903, 0.974, 1, "SELECT"), R(0.930, 0.951, 0.959, 0.974, 1, "BACK"));
        Assert.Equal(ScreenMode.Menu, r.Mode);
        Assert.Empty(r.Blocks);
        Assert.Null(r.Phone);
    }

    // --- Oyun modu ---

    [Fact]
    public void Frame0086_InfoBoxWithIndentedSecondLine()
    {
        var r = A(PsToast, R(0.689, 0.098, 0.837, 0.199, 0.5f, "FLEECA"),
            R(0.025, 0.282, 0.218, 0.308, 1, "Call Joey from your Contacts"), R(0.048, 0.312, 0.225, 0.339, 1, "or check the engine to tow"),
            R(0.025, 0.344, 0.176, 0.370, 1, "and repair your vehicle."), R(0.685, 0.470, 0.705, 0.488, 0.5f, "58 ft"));
        Assert.Equal(ScreenMode.Game, r.Mode);
        var b = Assert.Single(r.Blocks);
        Assert.Equal(ScreenKind.Info, b.Kind);
        Assert.Equal("Call Joey from your Contacts or check the engine to tow and repair your vehicle.", b.Text);
    }

    [Fact]
    public void Frame0114_NotifyWithSender()
    {
        var r = A(PsToast, R(0.802, 0.098, 0.815, 0.121, 0.5f, "FL"), R(0.330, 0.336, 0.356, 0.354, 0.5f, "FLEECA"),
            R(0.249, 0.176, 0.326, 0.202, 0.5f, "BANK"), R(0.438, 0.881, 0.563, 0.915, 1, "Mind if I drive?"),
            R(0.831, 0.811, 0.871, 0.830, 1, "Jason"), R(0.805, 0.850, 0.965, 0.876, 1, "Hey I'm at the bank. Let's"),
            R(0.804, 0.881, 0.930, 0.907, 1, "set up this account."), R(0.898, 0.940, 0.951, 0.964, 1, "→ Lucia"));
        var b = Assert.Single(r.Blocks);
        Assert.Equal(ScreenKind.Notify, b.Kind);
        Assert.Equal("Hey I'm at the bank. Let's set up this account.", b.Text);
        Assert.Equal("Jason", b.Speaker);
    }

    [Fact]
    public void Frames0065And0115_OtherMessages()
    {
        var r65 = A(R(0.823, 0.814, 0.869, 0.835, 0.5f, "Kenton"), R(0.798, 0.863, 0.955, 0.884, 1, "Still can't believe u found"),
            R(0.797, 0.897, 0.969, 0.922, 1, "them doubloons. U got that"), R(0.795, 0.928, 0.972, 0.953, 1, "luck boy. Need u out there..."));
        Assert.Equal("Still can't believe u found them doubloons. U got that luck boy. Need u out there...", r65.Blocks[0].Text);
        Assert.Equal("Kenton", r65.Blocks[0].Speaker);
        var r115 = A(R(0.833, 0.868, 0.869, 0.889, 1, "Lucia"), R(0.808, 0.904, 0.969, 0.933, 1, "OMW get started without"), R(0.810, 0.941, 0.834, 0.959, 1, "me."));
        Assert.Equal("OMW get started without me.", r115.Blocks[0].Text);
        Assert.Equal("Lucia", r115.Blocks[0].Speaker);
    }

    [Fact]
    public void Frame0022_TopRightNotify_CallEndedIgnored()
    {
        var r = ScreenText.Analyze(Lines([PsToast], Hud, [R(0.869, 0.258, 0.956, 0.284, 1, "Map updated"), R(0.680, 0.745, 0.744, 0.831, 0.5f, "Gianaás"),
            R(0.823, 0.907, 0.859, 0.930, 1, "Lucia"), R(0.823, 0.940, 0.891, 0.964, 1, "Call Ended")]));
        var b = Assert.Single(r.Blocks);
        Assert.Equal("Map updated", b.Text);
        Assert.Equal(ScreenKind.Notify, b.Kind);
        Assert.Equal("notify-top", b.Slot);
    }

    [Fact]
    public void TopRightAndSenderlessNotifications()
    {
        Assert.Equal(["Fuel low"], Texts(A(R(0.907, 0.384, 0.962, 0.412, 1, "Fuel low"))));
        Assert.Equal(["'70 Ganado has been repaired"], Texts(A(R(0.791, 0.385, 0.984, 0.413, 1, "'70 Ganado has been repaired"))));
        var r23 = A(R(0.826, 0.904, 0.958, 0.931, 1, "Workout Incomplete"), R(0.825, 0.936, 0.891, 0.963, 1, "Gains lost"));
        Assert.Equal("Workout Incomplete Gains lost", r23.Blocks[0].Text);
        Assert.Null(r23.Blocks[0].Speaker);
    }

    [Fact]
    public void Objectives_SignsAndCapsExcluded()
    {
        var r116 = A(PsToast, R(0.077, 0.005, 0.244, 0.101, 1, "FLEECA"), R(0.419, 0.106, 0.446, 0.119, 1, "\"NOTICE"),
            R(0.375, 0.946, 0.625, 0.977, 1, "Ask about setting up a bank account"));
        var b = Assert.Single(r116.Blocks);
        Assert.Equal(ScreenKind.Objective, b.Kind);
        Assert.Equal("Ask about setting up a bank account", b.Text);
        var r124 = A(R(0.424, 0.945, 0.576, 0.982, 1, "Speak to the manager"), R(0.872, 0.035, 1.001, 0.107, 1, "NOTICE"),
            R(0.858, 0.223, 0.999, 0.268, 1, "ALL ACTIVITIES"), R(0.870, 0.262, 0.984, 0.295, 1, "MONITORED"));
        Assert.Equal(["Speak to the manager"], Texts(r124));
        Assert.Equal(ScreenKind.Objective, A(R(0.400, 0.946, 0.600, 0.977, 1, "Get in your car")).Blocks[0].Kind);
        Assert.Equal(ScreenKind.Objective, A(R(0.350, 0.946, 0.650, 0.977, 1, "Take Lucia to the community center")).Blocks[0].Kind);
    }

    [Fact]
    public void SignsAndBillboards_TranslateNothing()
    {
        Assert.Empty(A(PsToast, R(0.109, 0.111, 0.156, 0.127, 1, "Sometimes"), R(0.109, 0.126, 0.166, 0.142, 1, "landlords just"),
            R(0.112, 0.142, 0.174, 0.158, 1, "can't get ahead."), R(0.521, 0.208, 0.577, 0.259, 1, "'NOTICE"), R(0.521, 0.322, 0.573, 0.373, 1, "ALL ACTIVITIES"),
            R(0.523, 0.344, 0.567, 0.390, 1, "MONITORED"), R(0.527, 0.366, 0.564, 0.410, 1, "BY VIDEO"), R(0.520, 0.390, 0.558, 0.424, 1, "• CAMERA")).Blocks);
        Assert.Empty(A(PsToast, R(0.467, 0.075, 0.645, 0.173, 1, "FLEEA"), R(0.496, 0.184, 0.606, 0.229, 1, "BANK"), R(0.711, 0.342, 0.790, 0.374, 1, "ALL ACTIVITIES")).Blocks);
        Assert.Empty(A(PsToast, R(0.097, 0.190, 0.186, 0.221, 1, "Why wait years"), R(0.099, 0.219, 0.147, 0.248, 1, "for your"), R(0.102, 0.245, 0.174, 0.272, 1, "inheritance?"),
            R(0.808, 0.457, 0.846, 0.473, 1, "Sometimes"), R(0.808, 0.470, 0.852, 0.483, 1, "landlords just"), R(0.807, 0.483, 0.856, 0.496, 1, "can't get ahead."),
            R(0.946, 0.625, 0.999, 0.643, 1, "Speak to an ach"), R(0.214, 0.568, 0.250, 0.592, 0.5f, "FLE CA")).Blocks);
        Assert.Empty(A(R(0.030, 0.300, 0.150, 0.326, 1, "can't get ahead.")).Blocks);
        Assert.Empty(A(R(0.547, 0.483, 0.797, 0.509, 1, "Lucia needs a bank account to get her"), R(0.547, 0.519, 0.801, 0.545, 1, "community service payments.")).Blocks);
        Assert.Empty(A(R(0.375, 0.946, 0.625, 0.977, 0.3f, "Ask about setting up a bank account")).Blocks);
        Assert.Empty(A(R(0.791, 0.385, 0.999, 0.413, 1, "'70 Ganado has been repaired")).Blocks);
        Assert.Empty(A(PsToast, R(0.033, 0.183, 0.164, 0.215, 1, "Key Lento Coffee Morning")).Blocks);
    }

    [Fact]
    public void ToastGpsMoneyClockCallUi_Ignored()
    {
        var junk = Lines([PsToast, R(0.40, 0.047, 0.60, 0.075, 1, "Remote Play connected"), R(0.03, 0.35, 0.12, 0.38, 1, "Turn Left"), R(0.03, 0.39, 0.08, 0.41, 1, "9 ft"),
            R(0.03, 0.42, 0.10, 0.44, 1, "0.56 mi"), R(0.417, 0.189, 0.467, 0.217, 1, "1:19 pm"),
            R(0.85, 0.86, 0.95, 0.88, 1, "Lucia"), R(0.85, 0.89, 0.97, 0.91, 1, "Incoming Call..."), R(0.88, 0.92, 0.94, 0.94, 1, "00:03")], Hud);
        Assert.Empty(ScreenText.Analyze(junk).Blocks);
    }

    [Fact]
    public void ButtonGlyphIsNormalized()
    {
        Assert.Equal("Press L1 to reply now", A(R(0.375, 0.946, 0.625, 0.977, 1, "Press LI to reply now")).Blocks[0].Text);
    }

    [Fact]
    public void NameLabelHelpers()
    {
        Assert.True(InfoBox.IsNameLabel("Key Lento Coffee Morning"));
        Assert.False(InfoBox.IsNameLabel("Get in your car"));
        Assert.False(InfoBox.IsNameLabel("Map updated"));
        Assert.False(InfoBox.IsNameLabel("Call Joey from your Contacts or check it."));
        Assert.Equal("No goods to sell", InfoBox.StripLeadingIcon("$ No goods to sell"));
        Assert.Equal("Hello there", InfoBox.StripLeadingIcon("i Hello there"));
        Assert.Equal("I think so", InfoBox.StripLeadingIcon("I think so"));
        Assert.Equal("Hello", InfoBox.StripLeadingIcon("Hello"));
        foreach (var sym in new[] { "$", "€", "©", "®", "i" })
            Assert.Equal("Hello world", InfoBox.StripLeadingIcon($"{sym} Hello world"));
    }

    // --- Telefon ---

    private static readonly Row[] F237 = [PsToast, R(0.436, 0.201, 0.493, 0.223, 1, "Suggested"), R(0.451, 0.244, 0.482, 0.267, 1, "Lucia"), R(0.450, 0.268, 0.573, 0.303, 1, "Hey! Get this, Kat and..."),
        R(0.445, 0.319, 0.483, 0.343, 1, "Donnie"), R(0.444, 0.342, 0.566, 0.381, 1, "SBW off Sunbelt High..."), R(0.437, 0.394, 0.479, 0.423, 1, "Kenton"),
        R(0.437, 0.417, 0.558, 0.459, 1, "Still can't believe u fo..."), R(0.393, 0.455, 0.447, 0.488, 1, "Messages"), R(0.429, 0.509, 0.458, 0.530, 1, "Brian"),
        R(0.426, 0.531, 0.549, 0.578, 1, "Remember, you get y..."), R(0.422, 0.587, 0.442, 0.607, 1, "Cal"), R(0.419, 0.606, 0.541, 0.655, 1, "And 2 here. Have fun..."),
        R(0.920, 0.902, 0.959, 0.925, 1, "SELECT"), R(0.930, 0.951, 0.961, 0.972, 1, "BACK")];

    private static readonly Row[] F238 = [PsToast, R(0.487, 0.199, 0.517, 0.220, 1, "Lucia"), R(0.424, 0.271, 0.491, 0.295, 1, "You got this!"), R(0.451, 0.378, 0.527, 0.411, 1, "New message"),
        R(0.407, 0.430, 0.526, 0.472, 1, "Hey! Get this, Kat and"), R(0.406, 0.458, 0.552, 0.505, 1, "Mattia are throwing a party"), R(0.403, 0.483, 0.545, 0.529, 1, "on their YACHT!?! Said we"),
        R(0.402, 0.510, 0.546, 0.562, 1, "could meet up at the Rusty"), R(0.399, 0.542, 0.527, 0.587, 1, "and go from there. That"), R(0.397, 0.574, 0.426, 0.592, 1, "cool?"),
        R(0.434, 0.634, 0.539, 0.677, 1, "Yeah, sure. I wanna"), R(0.429, 0.665, 0.516, 0.707, 1, "see this dinghy."), R(0.420, 0.719, 0.515, 0.763, 1, "You wanna go we"), R(0.417, 0.752, 0.458, 0.778, 1, "can go."),
        R(0.919, 0.853, 0.959, 0.876, 1, "SCROLL"), R(0.930, 0.901, 0.959, 0.926, 1, "SEND"), R(0.930, 0.951, 0.961, 0.972, 1, "BACK")];

    private static readonly Row[] LiveRows = [R(0.490, 0.140, 0.540, 0.163, 1, "Linda"),
        R(0.466, 0.200, 0.560, 0.224, 1, "As long as you pay"), R(0.466, 0.224, 0.540, 0.248, 1, "your rent. L."),
        R(0.466, 0.300, 0.570, 0.324, 1, "See me ASAP to"), R(0.466, 0.324, 0.560, 0.348, 1, "discuss. L."),
        R(0.545, 0.410, 0.676, 0.434, 1, "Okay. Thanks."),
        R(0.466, 0.490, 0.570, 0.514, 1, "Rent. Remember it."), R(0.466, 0.514, 0.570, 0.538, 1, "Have a nice day. L."),
        R(0.540, 0.610, 0.676, 0.634, 1, "Yeah I haven't"), R(0.540, 0.634, 0.620, 0.658, 1, "forgotten."), R(0.930, 0.951, 0.961, 0.972, 1, "BACK")];

    [Fact]
    public void Frame0237_PhoneListTranslatesNothing()
    {
        var r = A(F237);
        Assert.Equal(ScreenMode.Phone, r.Mode);
        Assert.Empty(r.Blocks);
        Assert.True(r.Phone!.IsList);
        var b = r.Phone.Box;
        Assert.True(b.MinX < 0.42 && b.MaxX > 0.55 && b.MinY < 0.21 && b.MaxY > 0.6, b.ToString());
    }

    [Fact]
    public void Frame0243_TiltedPhoneList()
    {
        var r = A(PsToast, R(0.522, 0.578, 0.568, 0.597, 1, "€ Suggested"), R(0.541, 0.607, 0.565, 0.620, 1, "Donnie"), R(0.539, 0.617, 0.611, 0.642, 0.5f, "SBW off Sunbelt High."),
            R(0.535, 0.649, 0.561, 0.661, 0.5f, "Kenton"), R(0.533, 0.660, 0.604, 0.684, 1, "Still can't believe u fo"), R(0.505, 0.678, 0.540, 0.701, 1, "Messages"),
            R(0.526, 0.711, 0.545, 0.724, 1, "Lucia"), R(0.524, 0.722, 0.601, 0.750, 1, "You wanna go we can ."), R(0.519, 0.752, 0.539, 0.767, 1, "Brian"),
            R(0.517, 0.764, 0.595, 0.797, 1, "Remember, you get y..."), R(0.513, 0.798, 0.526, 0.811, 1, "Cal"), R(0.509, 0.811, 0.585, 0.840, 1, "And 2 here. Have fun."),
            R(0.505, 0.839, 0.572, 0.873, 1, "Chuck's Gun Shop"), R(0.504, 0.857, 0.559, 0.884, 1, "Thanks Andrew."), R(0.498, 0.887, 0.530, 0.911, 0.5f, "BuckMe"),
            R(0.496, 0.902, 0.575, 0.940, 1, "Welcome to BuckMe,"), R(0.920, 0.901, 0.961, 0.926, 1, "SELECT"), R(0.930, 0.948, 0.978, 0.974, 1, "BACK O"));
        Assert.Equal(ScreenMode.Phone, r.Mode);
        Assert.Empty(r.Blocks);
        Assert.True(r.Phone!.IsList);
    }

    [Fact]
    public void SyntheticConversation_BubblesBecomeOnePhoneBlock()
    {
        var convo = Lines([R(0.60, 0.20, 0.72, 0.23, 1, "Hey, you around"), R(0.60, 0.23, 0.70, 0.26, 1, "later today?"),
            R(0.66, 0.34, 0.78, 0.37, 1, "Yeah, I can be there"), R(0.66, 0.37, 0.76, 0.40, 1, "in ten minutes."),
            R(0.60, 0.48, 0.72, 0.51, 1, "Great, bring the car."), R(0.66, 0.60, 0.78, 0.63, 1, "On my way now"), R(0.930, 0.951, 0.961, 0.972, 1, "BACK")]);
        var r = ScreenText.Analyze(convo);
        Assert.Equal(ScreenMode.Phone, r.Mode);
        Assert.False(r.Phone!.IsList);
        var b = Assert.Single(r.Blocks);
        Assert.Equal(ScreenKind.Phone, b.Kind);
        Assert.Equal("phone", b.Slot);
        Assert.Equal(["Hey, you around later today?", "Yeah, I can be there in ten minutes.", "Great, bring the car.", "On my way now"], b.Messages.Select(m => m.Text));
    }

    [Fact]
    public void Frame0238_Conversation_SpeakerAndMessages()
    {
        var r = A(F238);
        var m = r.Blocks.FirstOrDefault()?.Messages ?? [];
        Assert.True(r.Mode == ScreenMode.Phone && !r.Phone!.IsList && r.Blocks.Count == 1 && r.Blocks[0].Kind == ScreenKind.Phone && m.Count == 3, string.Join("|", m.Select(x => x.Text)));
        Assert.Equal("You got this!", m[0].Text);
        Assert.Equal("Lucia", r.Blocks[0].Speaker);
        Assert.StartsWith("Hey! Get this, Kat and Mattia are throwing a party", m[1].Text);
        Assert.EndsWith("That cool?", m[1].Text);
    }

    [Fact]
    public void LiveChatGeometry_SidesByPosition()
    {
        var r = A(LiveRows);
        Assert.True(r.Mode == ScreenMode.Phone && !r.Phone!.IsList && r.Blocks.Count == 1 && r.Blocks[0].Slot == "phone");
        var m = r.Blocks[0].Messages;
        Assert.Equal(["As long as you pay your rent. L.", "See me ASAP to discuss. L.", "Okay. Thanks.", "Rent. Remember it. Have a nice day. L.", "Yeah I haven't forgotten."], m.Select(x => x.Text));
        Assert.Equal([false, false, true, false, true], m.Select(x => x.FromMe));
        Assert.Equal("Linda", r.Blocks[0].Speaker);
        Assert.Equal(string.Join('\n', m.Select(x => x.Text)), r.Blocks[0].Text);
    }

    [Fact]
    public void BubbleColorHint_OverridesPositionRule()
    {
        // Kare 0238'de son seçenek balonunun merkezi telefon merkezinin SOLUNDA: konum kuralı yanılır, renk doğru.
        var colored = ScreenText.Analyze(Lines(F238), box => box.MinY >= 0.62);
        var m = colored.Blocks[0].Messages;
        Assert.Equal([false, false, true, true], m.Select(x => x.FromMe));
        Assert.Equal(["You got this!", "Hey! Get this, Kat and Mattia are throwing a party on their YACHT!?! Said we could meet up at the Rusty and go from there. That cool?",
            "Yeah, sure. I wanna see this dinghy.", "You wanna go we can go."], m.Select(x => x.Text));
        Assert.Equal("Lucia", colored.Blocks[0].Speaker);
        Assert.DoesNotContain(m, x => x.Text.Contains("new message", StringComparison.OrdinalIgnoreCase) || x.Text == "Lucia");
        // İpucu yok / null döner -> konum kuralı
        var plain = A(F238).Blocks[0].Messages;
        var nullHint = ScreenText.Analyze(Lines(F238), _ => null).Blocks[0].Messages;
        Assert.Equal(plain, nullHint);
        Assert.Equal([false, false, true, false, true], ScreenText.Analyze(Lines(LiveRows), _ => null).Blocks[0].Messages.Select(x => x.FromMe));
        Assert.Null(new ScreenLine("x", ScreenBox.Zero).BlueBubble);
    }

    [Fact]
    public void IsMineRule()
    {
        var box = new ScreenBox(0.450, 0.122, 0.247, 0.613);
        Assert.False(ScreenText.IsMine(new ScreenBox(0.47, 0.3, 0.10, 0.05), box));
        Assert.True(ScreenText.IsMine(new ScreenBox(0.58, 0.3, 0.09, 0.05), box));
        Assert.False(ScreenText.IsMine(new ScreenBox(0.50, 0.3, 0.10, 0.05), box));
    }

    // --- İzleyici ---

    private static ScreenBlock Blk(ScreenKind kind, string text, string? slot = null) =>
        new(kind, text, null, null, ScreenBox.Zero, 0.02, slot ?? kind.ToString().ToLowerInvariant());

    private static void NoActions(List<ScreenAction> a) => Assert.Empty(a);
    private static void Only(ScreenAction expected, List<ScreenAction> a) => Assert.Equal(new[] { expected }, a);

    [Fact]
    public void Tracker_StableTwoPassesThenNoResend()
    {
        var tr = new ScreenTracker();
        var ob = Blk(ScreenKind.Objective, "Ask about setting up a bank account");
        NoActions(tr.Feed([ob], ScreenMode.Game, 0));
        Only(new ScreenAction.Translate(ob), tr.Feed([ob], ScreenMode.Game, 1));
        NoActions(tr.Feed([ob], ScreenMode.Game, 2));
        NoActions(tr.Feed([Blk(ScreenKind.Objective, "Ask about setting up a bank acount")], ScreenMode.Game, 3));
        NoActions(tr.Feed([], ScreenMode.Game, 4));
        NoActions(tr.Feed([ob], ScreenMode.Game, 5));
        NoActions(tr.Feed([], ScreenMode.Game, 6));
        Only(new ScreenAction.Clear("objective", ScreenKind.Objective), tr.Feed([], ScreenMode.Game, 8.1));
        NoActions(tr.Feed([ob], ScreenMode.Game, 9));
        Only(new ScreenAction.Translate(ob), tr.Feed([ob], ScreenMode.Game, 10));
        var ob2 = Blk(ScreenKind.Objective, "Speak to the manager");
        NoActions(tr.Feed([ob2], ScreenMode.Game, 11));
        Only(new ScreenAction.Translate(ob2), tr.Feed([ob2], ScreenMode.Game, 12));
    }

    [Fact]
    public void Tracker_SinglePassFlickerNotTranslated()
    {
        var tr = new ScreenTracker();
        NoActions(tr.Feed([Blk(ScreenKind.Info, "Some flicker text here")], ScreenMode.Game, 0));
        NoActions(tr.Feed([], ScreenMode.Game, 1));
        NoActions(tr.Feed([Blk(ScreenKind.Info, "Other thing entirely different")], ScreenMode.Game, 2));
    }

    [Fact]
    public void Tracker_ModeChangeClearsDescriptionAfterTwoPasses()
    {
        var tr = new ScreenTracker();
        var ds = Blk(ScreenKind.Description, "Fill up on fuel or quickly service your vehicle.");
        tr.Feed([ds], ScreenMode.Map, 0);
        Only(new ScreenAction.Translate(ds), tr.Feed([ds], ScreenMode.Map, 1));
        NoActions(tr.Feed([], ScreenMode.Game, 2));
        Only(new ScreenAction.Clear("description", ScreenKind.Description), tr.Feed([], ScreenMode.Game, 3));
    }

    [Fact]
    public void Tracker_SinglePassMenuDoesNotDropObjective()
    {
        var tr = new ScreenTracker();
        var ob = Blk(ScreenKind.Objective, "Get in your car");
        tr.Feed([ob], ScreenMode.Game, 0); tr.Feed([ob], ScreenMode.Game, 1);
        NoActions(tr.Feed([ob], ScreenMode.Menu, 2));
        NoActions(tr.Feed([ob], ScreenMode.Game, 3));
        NoActions(tr.Feed([], ScreenMode.Menu, 4));
        Only(new ScreenAction.Clear("objective", ScreenKind.Objective), tr.Feed([], ScreenMode.Menu, 5));
    }

    [Fact]
    public void Tracker_SeparateSlotsAreIndependent()
    {
        var tr = new ScreenTracker();
        var nb = Blk(ScreenKind.Notify, "Map updated", "notify-top");
        var nm = Blk(ScreenKind.Notify, "Hey I'm at the bank.", "notify-bottom");
        tr.Feed([nb, nm], ScreenMode.Game, 0);
        Assert.Equal(2, tr.Feed([nb, nm], ScreenMode.Game, 1).Count);
    }

    [Fact]
    public void Tracker_SlidingNotificationUpdatesOnce()
    {
        var tr = new ScreenTracker();
        var p1 = Blk(ScreenKind.Notify, "Hey I'm at the bank. Let", "notify-bottom");
        var p2 = Blk(ScreenKind.Notify, "Hey I'm at the bank. Let's set up this account.", "notify-bottom");
        tr.Feed([p1], ScreenMode.Game, 0);
        Only(new ScreenAction.Translate(p1), tr.Feed([p1], ScreenMode.Game, 1));
        var a = tr.Feed([p2], ScreenMode.Game, 2).Concat(tr.Feed([p2], ScreenMode.Game, 3)).ToList();
        Assert.Equal(new ScreenAction[] { new ScreenAction.Translate(p2) }, a);
        NoActions(tr.Feed([p2], ScreenMode.Game, 4));
    }

    private static readonly ScreenBox PBox = new(0.450, 0.122, 0.247, 0.613);

    private static ScreenBlock PhoneBlock(params (bool, string)[] msgs)
    {
        var m = msgs.Select(x => new PhoneMessage(x.Item1, x.Item2)).ToList();
        return new ScreenBlock(ScreenKind.Phone, string.Join('\n', m.Select(x => x.Text)), null, "Linda", PBox, 0.024, "phone") { Messages = m };
    }

    [Fact]
    public void Tracker_PhoneNewMessageResendsButJitterDoesNot()
    {
        var tr = new ScreenTracker();
        (bool, string)[] baseMsgs = [(false, "As long as you pay your rent. L."), (false, "See me ASAP to discuss. L."), (true, "Okay. Thanks.")];
        var pb1 = PhoneBlock(baseMsgs);
        NoActions(tr.Feed([pb1], ScreenMode.Phone, 0));
        Only(new ScreenAction.Translate(pb1), tr.Feed([pb1], ScreenMode.Phone, 1));
        NoActions(tr.Feed([pb1], ScreenMode.Phone, 3));
        (bool, string)[] jitter = [baseMsgs[0], (false, "See me ASAP to discuss. L"), baseMsgs[2]];
        NoActions(tr.Feed([PhoneBlock(jitter)], ScreenMode.Phone, 4));
        NoActions(tr.Feed([PhoneBlock(jitter)], ScreenMode.Phone, 5));
        var pb2 = PhoneBlock([.. baseMsgs, (false, "Rent. Remember it. Have a nice day. L.")]);
        Assert.Equal(new ScreenAction[] { new ScreenAction.Translate(pb2) }, tr.Feed([pb2], ScreenMode.Phone, 6).Concat(tr.Feed([pb2], ScreenMode.Phone, 7)).ToList());
        var pb3 = PhoneBlock([.. baseMsgs, (false, "Rent. Remember it. Have a nice day. L."), (true, "Ok.")]);
        Assert.Equal(new ScreenAction[] { new ScreenAction.Translate(pb3) }, tr.Feed([pb3], ScreenMode.Phone, 8).Concat(tr.Feed([pb3], ScreenMode.Phone, 9)).ToList());
        NoActions(tr.Feed([], ScreenMode.Phone, 20));
        Only(new ScreenAction.Clear("phone", ScreenKind.Phone), tr.Feed([], ScreenMode.Phone, 22.5));
    }

    [Fact]
    public void Tracker_TrailingWordFlicker()
    {
        ScreenBlock Nb(string t) => new(ScreenKind.Notify, t, null, null, ScreenBox.Zero, 0.02, "notify-bottom");
        var tr = new ScreenTracker();
        var full = Nb("SBW off Sunbelt Highway cmon"); var shortB = Nb("SBW off Sunbelt Highway");
        var sends = 0;
        void F(ScreenTracker t, ScreenBlock b, double at) { sends += t.Feed([b], ScreenMode.Game, at).Count(a => a is ScreenAction.Translate); }
        F(tr, shortB, 0); F(tr, shortB, 1);
        Assert.Equal(1, sends);
        F(tr, full, 2); F(tr, full, 3);   // 5 karakter uzadı: yeniden gönderilir
        Assert.Equal(2, sends);
        for (var i = 0; i < 6; i++) F(tr, i % 2 == 0 ? shortB : full, 4 + i);
        Assert.Equal(2, sends);
        var tr2 = new ScreenTracker();
        sends = 0;
        F(tr2, full, 0); F(tr2, full, 1); F(tr2, shortB, 2); F(tr2, shortB, 3);
        Assert.Equal(1, sends);
        Assert.Equal(ScreenMode.Game, tr2.CurrentMode);
        var tr3 = new ScreenTracker();
        tr3.Feed([], ScreenMode.Menu, 0); tr3.Feed([], ScreenMode.Menu, 1);
        Assert.Equal(ScreenMode.Menu, tr3.CurrentMode);
    }

    // --- DialogueGate ---

    private static ScreenLine SL(double x0, double y0, double x1, double y1, string t, float c = 1) => new(t, new ScreenBox(x0, y0, x1 - x0, y1 - y0), c);

    private static readonly ScreenLine[] F84 = [SL(0.020, 0.829, 0.061, 0.853, "0.15 mi"), SL(0.243, 0.828, 0.757, 0.877, "Don't wanna ask and then have to say no 'cause of some case"),
        SL(0.436, 0.884, 0.565, 0.915, "officer bullshit."), SL(0.923, 0.894, 0.985, 0.931, "THROW O"), SL(0.907, 0.948, 0.980, 0.977, "PUT AWAY")];

    [Fact]
    public void DialogueGate_Reasons()
    {
        var g = DialogueGate.Evaluate([F84[1], F84[2]], []);
        Assert.Equal([0, 1], g.Kept);
        var leak = new[] { SL(0.404, 0.763, 0.531, 0.820, "Welcome to BuckMe, ..."), SL(0.790, 0.800, 0.980, 0.830, "Hey! Ge are thro"), SL(0.412, 0.884, 0.592, 0.917, "Yeah, well, you know..."),
            SL(0.470, 0.850, 0.530, 0.880, "Hey! Get this, Kat and..."), SL(0.470, 0.850, 0.640, 0.880, "Look at this mess") };
        Assert.Equal(DialogueGate.Reason.EllipsisPreview, DialogueGate.ReasonFor(leak[0], []));
        Assert.Equal(DialogueGate.Reason.InsideExcluded, DialogueGate.ReasonFor(leak[1], [new ScreenBox(0.79, 0.80, 0.19, 0.17)]));
        Assert.Equal(DialogueGate.Reason.OffCenter, DialogueGate.ReasonFor(leak[1], []));
        Assert.Null(DialogueGate.ReasonFor(leak[2], []));
        Assert.Equal(DialogueGate.Reason.EllipsisPreview, DialogueGate.ReasonFor(leak[3], []));
        Assert.Equal(DialogueGate.Reason.InsideExcluded, DialogueGate.ReasonFor(leak[4], [new ScreenBox(0.40, 0.20, 0.2, 0.7)]));
        Assert.Null(DialogueGate.ReasonFor(new ScreenLine("A very long line that spans wide of the screen", new ScreenBox(0.05, 0.85, 0.5, 0.03)), []));
        Assert.Equal(DialogueGate.Reason.ObjectiveBand, DialogueGate.ReasonFor(SL(0.4, 0.95, 0.6, 0.98, "Speak to the manager"), []));
    }

    [Fact]
    public void DialogueGate_CrossCheck()
    {
        var full = F84.Append(new ScreenLine("A Uzaktan Oynama baglandi.", new ScreenBox(0.411, 0.047, 0.178, 0.028), 0.5f)).ToList();
        var cc = DialogueGate.CrossCheck("Sh, Don't wanna ask and then have to say no cause of some case officer bullshit", full, []);
        Assert.Equal("Don't wanna ask and then have to say no 'cause of some case officer bullshit.", cc?.Text);
        Assert.True(cc!.Value.Rect.MinY < 0.83 && cc.Value.Rect.MaxY > 0.91);
        Assert.Null(DialogueGate.CrossCheck("Completely unrelated words appear in the crop text now", full, []));
        Assert.Null(DialogueGate.CrossCheck("Don't wanna ask and then have to say no", full, [new ScreenBox(0, 0.7, 1, 0.25)]));
    }
}
