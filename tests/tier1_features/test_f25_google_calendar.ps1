# test_f25_google_calendar.ps1
# Feature 25: Google Calendar OAuth2 Integration & UI

$commonDir = Join-Path $PSScriptRoot "..\common"
. (Join-Path $commonDir "TestHarness.ps1")
. (Join-Path $commonDir "DeskmateLoader.ps1")

Describe "Feature 25: Google Calendar Integration" {
    $islandType = Get-DeskmateType "Island"
    $prefsType = Get-DeskmateType "PrefsWindow"
    $langType = Get-DeskmateType "Lang"
    $vaultType = Get-DeskmateType "GoogleCalVault"
    $srcType = Get-DeskmateType "GoogleCalendarSource"
    $authType = Get-DeskmateType "GoogleCalAuth"
    $jsonType = Get-DeskmateType "Json"
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Static,Instance'

    $vaultFile = $vaultType.GetField("FilePath", $flags).GetValue($null)
    $hadVault = Test-Path $vaultFile
    $backupVault = $null
    if ($hadVault) {
        $backupVault = [System.IO.File]::ReadAllBytes($vaultFile)
    }

    try {
        It "1. GoogleCalVault Save/Load/Forget DPAPI Round-Trip" {
            $vaultType.GetMethod("Forget", $flags).Invoke($null, $null) | Out-Null
            Assert-False (Test-Path $vaultFile) "Forget should remove vault file"

            $v = [Activator]::CreateInstance($vaultType)
            $v.ClientId = "test-client-id.apps.googleusercontent.com"
            $v.ClientSecret = "GOCSPX-test-secret"
            $v.RefreshToken = "1//test-refresh-token"
            $v.Email = "user@example.com"
            $v.Save()

            Assert-True (Test-Path $vaultFile) "Save should create encrypted vault file"

            $loaded = $vaultType.GetMethod("Load", $flags).Invoke($null, $null)
            Assert-Equal $loaded.ClientId "test-client-id.apps.googleusercontent.com" "ClientId should round-trip"
            Assert-Equal $loaded.ClientSecret "GOCSPX-test-secret" "ClientSecret should round-trip"
            Assert-Equal $loaded.RefreshToken "1//test-refresh-token" "RefreshToken should round-trip"
            Assert-Equal $loaded.Email "user@example.com" "Email should round-trip"
            Assert-True $loaded.HasRefreshToken "HasRefreshToken should be true"

            $vaultType.GetMethod("Forget", $flags).Invoke($null, $null) | Out-Null
            Assert-False (Test-Path $vaultFile) "Forget should delete vault file"

            $afterForget = $vaultType.GetMethod("Load", $flags).Invoke($null, $null)
            Assert-False $afterForget.HasRefreshToken "HasRefreshToken should be false after Forget"
            Assert-Equal $afterForget.ClientId "" "ClientId should be empty after Forget"
        }

        It "2. GoogleCalendarSource.ParseEvents: Timed + HangoutLink, All-Day, Cancelled" {
            $today = [DateTime]::Today
            $t1Start = $today.AddHours(10).ToString("yyyy-MM-ddTHH:mm:sszzz", [System.Globalization.CultureInfo]::InvariantCulture)
            $t1End = $today.AddHours(11).ToString("yyyy-MM-ddTHH:mm:sszzz", [System.Globalization.CultureInfo]::InvariantCulture)
            $allDayStart = $today.ToString("yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture)
            $allDayEnd = $today.AddDays(1).ToString("yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture)

            $jsonPayload = @"
{
  "items": [
    {
      "status": "confirmed",
      "id": "evt_standup_1",
      "summary": "Standup Tim",
      "hangoutLink": "https://meet.google.com/abc-defg-hij",
      "start": { "dateTime": "$t1Start" },
      "end": { "dateTime": "$t1End" }
    },
    {
      "status": "confirmed",
      "summary": "Libur Nasional",
      "htmlLink": "https://calendar.google.com/calendar/event?eid=national-holiday",
      "start": { "date": "$allDayStart" },
      "end": { "date": "$allDayEnd" }
    },
    {
      "status": "cancelled",
      "summary": "Rapat Dibatalkan",
      "start": { "dateTime": "$t1Start" },
      "end": { "dateTime": "$t1End" }
    }
  ]
}
"@
            $parseJson = $jsonType.GetMethod("Parse", $flags)
            $root = $parseJson.Invoke($null, @($jsonPayload))
            Assert-NotNull $root "Json.Parse should parse sample Google Calendar response"

            $parseEvents = $srcType.GetMethod("ParseEvents", $flags)
            $slots = $parseEvents.Invoke($null, @($root))
            Assert-Equal $slots.Count 2 "Cancelled event should be skipped; 2 active events expected"

            $timed = $slots[0]
            Assert-Equal $timed.Title "Standup Tim" "Timed event title should match"
            Assert-True $timed.Online "Timed event with hangoutLink should have Online = true"
            Assert-False $timed.AllDay "Timed dateTime event should have AllDay = false"
            Assert-Equal $timed.Source "google" "Source should be 'google'"
            Assert-Equal $timed.Url "https://meet.google.com/abc-defg-hij" "Url should match hangoutLink"
            Assert-Equal $timed.Id "evt_standup_1" "Timed event Id should be populated from JSON id"

            $allDay = $slots[1]
            Assert-Equal $allDay.Title "Libur Nasional" "All-day event title should match"
            Assert-True $allDay.AllDay "All-day date event should have AllDay = true"
            Assert-Equal $allDay.Source "google" "All-day Source should be 'google'"
            Assert-False $allDay.Online "All-day event without meeting link should have Online = false"
            Assert-Equal "" $allDay.Url "Google Calendar htmlLink must not become an event destination"
        }

        It "Google events expose only explicit web destinations" {
            $parse = $jsonType.GetMethod("Parse", $flags)
            $parseEvents = $srcType.GetMethod("ParseEvents", $flags)
            $day = [DateTime]::Today.ToString("yyyy-MM-dd")
            $cases = @(
                @{ fields = @{ source = @{ url = "https://example.org/register" } }; url = "https://example.org/register" },
                @{ fields = @{ location = "https://example.org/venue" }; url = "https://example.org/venue" },
                @{ fields = @{ location = "https://example.org/venue Room 4" }; url = "" },
                @{ fields = @{ source = @{ url = "https://calendar.google.com/calendar/event?eid=example" } }; url = "" },
                @{ fields = @{ conferenceData = @{ entryPoints = @(
                    @{ entryPointType = "phone"; uri = "tel:+123456789" },
                    @{ entryPointType = "video"; uri = "https://example.org/conference" }
                ) } }; url = "https://example.org/conference" }
            )
            foreach ($case in $cases) {
                $ev = $case.fields
                $ev.id = "sample-event"
                $ev.start = @{ date = $day }
                $ev.htmlLink = "https://calendar.google.com/calendar/event?eid=example"
                $json = @{ items = @($ev) } | ConvertTo-Json -Depth 10 -Compress
                $root = $parse.Invoke($null, @([string]$json))
                $slots = $parseEvents.Invoke($null, @($root))
                Assert-Equal 1 $slots.Count "A no-link event must still appear in the calendar"
                Assert-Equal $case.url $slots[0].Url "Unexpected event destination for $json"
            }
        }

        It "3. PrefsWindow.Go('integrasi') + Relayout() and Lang.T Translations" {
            $isle = [Activator]::CreateInstance($islandType)
            try {
                $prefs = [Activator]::CreateInstance($prefsType, @($isle))
                try {
                    $goMethod = $prefsType.GetMethod("Go", $flags)
                    $goMethod.Invoke($prefs, @("integrasi")) | Out-Null
                    $relayout = $prefsType.GetMethod("Relayout", $flags)
                    $relayout.Invoke($prefs, $null) | Out-Null
                } finally {
                    $prefs.Dispose()
                }
            } finally {
                $isle.Dispose()
            }

            $engField = $langType.GetField("English", $flags)
            $prevEng = $engField.GetValue($null)
            try {
                $engField.SetValue($null, $true)
                $tMethod = $langType.GetMethod("T", $flags)
                Assert-Equal ($tMethod.Invoke($null, @("Akun Google"))) "Google Account" "Lang.T should translate 'Akun Google'"
                Assert-Equal ($tMethod.Invoke($null, @("Sinkronkan sekarang"))) "Sync now" "Lang.T should translate 'Sinkronkan sekarang'"
                Assert-Equal ($tMethod.Invoke($null, @("Google Calendar tersambung"))) "Google Calendar connected" "Lang.T should translate 'Google Calendar tersambung'"
                Assert-Equal ($tMethod.Invoke($null, @("Putuskan"))) "Disconnect" "Lang.T should translate 'Putuskan'"
                Assert-Equal ($tMethod.Invoke($null, @("+ Acara"))) "+ Event" "Lang.T should translate '+ Acara'"
                Assert-Equal ($tMethod.Invoke($null, @("Acara baru"))) "New event" "Lang.T should translate 'Acara baru'"
                Assert-Equal ($tMethod.Invoke($null, @("Sepanjang hari"))) "All day" "Lang.T should translate 'Sepanjang hari'"
                Assert-Equal ($tMethod.Invoke($null, @("Hapus acara"))) "Delete event" "Lang.T should translate 'Hapus acara'"
                Assert-Equal ($tMethod.Invoke($null, @("Kredensial OAuth"))) "OAuth credentials" "Lang.T should translate 'Kredensial OAuth'"
                Assert-Equal ($tMethod.Invoke($null, @("Tidak ada acara"))) "No events" "Lang.T should translate 'Tidak ada acara'"
                Assert-Equal ($tMethod.Invoke($null, @("Seharian"))) "All day" "Lang.T should translate 'Seharian'"
            } finally {
                $engField.SetValue($null, $prevEng)
            }
        }
        It "4. Read-Write Google Calendar API Surface & Scope" {
            $scopeField = $authType.GetField("Scope", $flags)
            $scopeVal = if ($null -ne $scopeField) { $scopeField.GetValue($null) } else { $authType.GetProperty("Scope", $flags).GetValue($null, $null) }
            Assert-True ($scopeVal -like "*calendar.events*") "GoogleCalAuth.Scope should contain 'calendar.events'"
            Assert-NotNull ($islandType.GetMethod("CreateGoogleCalendarEvent", $flags)) "Island should expose CreateGoogleCalendarEvent"
            Assert-NotNull ($islandType.GetMethod("DeleteGoogleCalendarEvent", $flags)) "Island should expose DeleteGoogleCalendarEvent"
        }
        It "5. GoogleCalVault EffectiveClientId Fallback & Custom Override" {
            $defaultId = $vaultType.GetField("DefaultClientId", $flags).GetValue($null)
            Assert-Equal $defaultId ([string]$env:DESKMATE_GOOGLE_CLIENT_ID).Trim() "DefaultClientId should come from the environment"

            $v = [Activator]::CreateInstance($vaultType)
            $v.ClientId = ""
            Assert-Equal $v.EffectiveClientId $defaultId "EffectiveClientId should fall back to DefaultClientId when ClientId is empty"

            $v.ClientId = "custom-id.apps.googleusercontent.com"
            Assert-Equal $v.EffectiveClientId "custom-id.apps.googleusercontent.com" "EffectiveClientId should use custom ClientId when set"
        }
        It "6. CalendarSheet.EventsForDay & Event Details Rendering" {
            $inkType = Get-DeskmateType "Ink"
            $slotType = Get-DeskmateType "ClassSlot"
            $calType = $islandType.GetNestedType("CalendarSheet", $flags)
            $dockType = $islandType.GetNestedType("DockWindow", $flags)
            [void]$inkType.GetMethod("Init", $flags).Invoke($null, $null)

            $isle = [Activator]::CreateInstance($islandType)
            $ctlActs = $islandType.GetMethod("ControlActs", $flags)
            if ($ctlActs) { [void]$ctlActs.Invoke($isle, $null) }
            $dock = [Activator]::CreateInstance($dockType, @($isle))
            $cal = [Activator]::CreateInstance($calType, @($isle, $dock))
            $bmp = New-Object System.Drawing.Bitmap 360, 800
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $today = [DateTime]::Today
                $tomorrow = $today.AddDays(1)
                $listType = $islandType.GetField("classes", $flags).FieldType
                $slots = [Activator]::CreateInstance($listType)

                $evTimed = [Activator]::CreateInstance($slotType)
                $evTimed.Title = "Daily Standup"
                $evTimed.Room = "Meet"
                $evTimed.Url = "https://meet.google.com/xyz"
                $evTimed.Start = $today.AddHours(9)
                $evTimed.End = $today.AddHours(10)
                $slots.Add($evTimed) | Out-Null

                $evAllDay = [Activator]::CreateInstance($slotType)
                $evAllDay.Title = "Konferensi"
                $evAllDay.Source = "google"
                $evAllDay.Id = "google-no-link"
                $evAllDay.AllDay = $true
                $evAllDay.Start = $today.AddDays(-1)
                $evAllDay.End = $today.AddDays(2)
                $slots.Add($evAllDay) | Out-Null

                $evOther = [Activator]::CreateInstance($slotType)
                $evOther.Title = "Besok Saja"
                $evOther.Start = $tomorrow.AddHours(14)
                $evOther.End = $tomorrow.AddHours(15)
                $slots.Add($evOther) | Out-Null

                $evOvernight = [Activator]::CreateInstance($slotType)
                $evOvernight.Title = "Deploy Tengah Malam"
                $evOvernight.Start = $today.AddHours(22)
                $evOvernight.End = $tomorrow.AddHours(2)
                $slots.Add($evOvernight) | Out-Null

                $islandType.GetField("classes", $flags).SetValue($isle, $slots)

                $eventsForDay = $calType.GetMethod("EventsForDay", $flags)
                $todayList = $eventsForDay.Invoke($cal, @($today))
                Assert-Equal $todayList.Count 3 "EventsForDay(today) should include timed, spanning all-day, and overnight events"
                $tomorrowList = $eventsForDay.Invoke($cal, @($tomorrow))
                Assert-Equal $tomorrowList.Count 3 "EventsForDay(tomorrow) should include spanning all-day, tomorrow, and overnight events"
                Assert-True (($todayList | Where-Object { $_.Title -eq "Deploy Tengah Malam" }).Count -eq 1) "EventsForDay(today) should contain overnight event"
                Assert-True (($tomorrowList | Where-Object { $_.Title -eq "Deploy Tengah Malam" }).Count -eq 1) "EventsForDay(tomorrow) should contain overnight event"
                $pastList = $eventsForDay.Invoke($cal, @($today.AddDays(-5)))
                Assert-Equal $pastList.Count 0 "EventsForDay(past) should be empty"

                $calType.GetField("g", $flags).SetValue($cal, $g)
                $painter = $calType.GetField("p", $flags).GetValue($cal)
                $painter.G = $g
                $painter.T = $inkType.GetField("Base", $flags).GetValue($null)

                $monRect = (New-Object System.Drawing.Rectangle(0, 0, 1920, 1080)).PSObject.BaseObject
                $opening = $calType.GetMethod("Opening", $flags)
                $opening.Invoke($cal, [object[]]@($monRect, 1000)) | Out-Null
                $eventAreaH = $calType.GetField("EventAreaH", $flags).GetValue($cal)
                Assert-Equal ([double]$eventAreaH) 96.0 "EventAreaH should be 96 when events exist"

                $calType.GetProperty("selectedDay", $flags).SetValue($cal, $today)
                $calType.GetMethod("Draw", $flags).Invoke($cal, $null) | Out-Null

                $hits = $calType.GetField("hits", $flags).GetValue($cal)
                $hasEv0 = $false
                foreach ($kv in $hits) {
                    if ($kv.Value -eq "ev0") { $hasEv0 = $true }
                }
                Assert-True $hasEv0 "DrawDayEvents should register 'ev0' hit target for event with URL"
                $noLinkIndex = 0
                while ($todayList[$noLinkIndex].Id -ne "google-no-link") { $noLinkIndex++ }
                $calType.GetMethod("Clicked", $flags).Invoke($cal, @("ev$noLinkIndex", [Windows.Forms.MouseButtons]::Left)) | Out-Null
            } finally {
                $calType.GetMethod("Teardown", $flags).Invoke($cal, $null) | Out-Null
                $g.Dispose()
                $bmp.Dispose()
                [System.ComponentModel.Component].GetMethod("Dispose", $flags, $null, [type[]]@(), $null).Invoke($cal, $null) | Out-Null
                $dock.Dispose()
                $isle.Dispose()
            }
        }
        It "7. Multi-Calendar ParseCalendarIds, nextPageToken & Widened Window ParseEvents" {
            $parseJson = $jsonType.GetMethod("Parse", $flags)
            $jsonStr = $jsonType.GetMethod("Str", $flags)
            $parseCalendarIds = $srcType.GetMethod("ParseCalendarIds", $flags)
            $parseEvents = $srcType.GetMethod("ParseEvents", $flags)
            $compareEvents = $srcType.GetMethod("CompareEventsByStart", $flags)

            $calListJson = @"
{
  "items": [
    { "id": "primary@example.com", "primary": true },
    { "id": "work@group.calendar.google.com" },
    { "id": "primary@example.com" },
    { "id": "" }
  ]
}
"@
            $calRoot = $parseJson.Invoke($null, @($calListJson))
            $calIds = $parseCalendarIds.Invoke($null, @($calRoot))
            Assert-Equal $calIds.Count 2 "ParseCalendarIds should deduplicate and skip empty IDs"
            Assert-Equal $calIds[0] "primary@example.com" "First calendar ID should match"
            Assert-Equal $calIds[1] "work@group.calendar.google.com" "Second calendar ID should match"

            $today = [DateTime]::Today
            $pastDayStart = $today.AddDays(-30).ToString("yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture)
            $pastDayEnd = $today.AddDays(-29).ToString("yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture)
            $futureDayStart = $today.AddDays(45).ToString("yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture)
            $futureDayEnd = $today.AddDays(46).ToString("yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture)

            $pagedJson = @"
{
  "nextPageToken": "page_token_2",
  "items": [
    {
      "status": "confirmed",
      "id": "evt_future_45",
      "summary": "Future Milestone",
      "start": { "date": "$futureDayStart" },
      "end": { "date": "$futureDayEnd" }
    },
    {
      "status": "confirmed",
      "id": "evt_past_30",
      "summary": "Past Review",
      "start": { "date": "$pastDayStart" },
      "end": { "date": "$pastDayEnd" }
    }
  ]
}
"@
            $pageRoot = $parseJson.Invoke($null, @($pagedJson))
            Assert-Equal ($jsonStr.Invoke($null, @($pageRoot, "nextPageToken"))) "page_token_2" "nextPageToken should be readable from paged response"

            $slots = $parseEvents.Invoke($null, @($pageRoot))
            Assert-Equal $slots.Count 2 "ParseEvents should include all-day events across -60..+180 day window"
            Assert-True ($compareEvents.Invoke($null, @($slots[1], $slots[0])) -lt 0) "CompareEventsByStart should order earlier events first"
        }
        It "8. Indonesian National Holidays: Portable, ParseHolidayIcs, MergeSources & CalendarSheet" {
            $portable = $islandType.GetField("Portable", $flags).GetValue($null)
            Assert-True ($portable.Contains("calendar.holidays")) "Island.Portable should contain 'calendar.holidays'"

            $holType = Get-DeskmateType "NationalHolidaysSource"
            $parseHoliday = $holType.GetMethod("ParseHolidayIcs", $flags)
            $ics = @"
BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
DTSTART;VALUE=DATE:20261225
DTEND;VALUE=DATE:20261226
SUMMARY:Hari Raya Natal
END:VEVENT
END:VCALENDAR
"@
            $from = [DateTime]::new(2026, 12, 1)
            $to = [DateTime]::new(2026, 12, 31)
            $parsed = $parseHoliday.Invoke($null, @($ics, $from, $to))
            Assert-Equal $parsed.Count 1 "ParseHolidayIcs should parse 1 holiday event"
            $natal = $parsed[0]
            Assert-Equal $natal.Title "Hari Raya Natal" "Parsed holiday title should match"
            Assert-Equal $natal.Start.Date ([DateTime]::new(2026, 12, 25)) "Parsed holiday Start.Date should be 2026-12-25"
            Assert-Equal $natal.Source "holiday" "Parsed holiday Source should be 'holiday'"
            Assert-Equal $natal.Kind "libur" "Parsed holiday Kind should be 'libur'"
            Assert-True $natal.AllDay "Parsed holiday AllDay should be true"

            $isle = [Activator]::CreateInstance($islandType)
            try {
                $hubType = Get-DeskmateType "SourceHub"
                $slotType = Get-DeskmateType "ClassSlot"
                $listType = $islandType.GetField("classes", $flags).FieldType
                $hub = [Activator]::CreateInstance($hubType, @([Action[Action]]{ param($a) }))
                $hub.Holidays.Enabled = $false
                Assert-Equal $hub.Any $false "SourceHub.Any should be false when Holidays.Enabled is false and no other sources are connected"
                $hub.Holidays.Enabled = $true
                Assert-Equal $hub.Any $true "SourceHub.Any should be true when Holidays.Enabled is true"

                $futureDay = [DateTime]::Today.AddDays(10)
                $holEvents = [Activator]::CreateInstance($listType)
                $hDup = [Activator]::CreateInstance($slotType)
                $hDup.Title = "Hari Raya Natal"
                $hDup.Start = $futureDay
                $hDup.End = $futureDay.AddDays(1)
                $hDup.AllDay = $true
                $hDup.Source = "holiday"
                $hDup.Kind = "libur"
                $holEvents.Add($hDup) | Out-Null

                $hUnique = [Activator]::CreateInstance($slotType)
                $hUnique.Title = "Cuti Bersama"
                $hUnique.Start = $futureDay.AddDays(1)
                $hUnique.End = $futureDay.AddDays(2)
                $hUnique.AllDay = $true
                $hUnique.Source = "holiday"
                $hUnique.Kind = "libur"
                $holEvents.Add($hUnique) | Out-Null
                $hub.Holidays.Events = $holEvents

                $gEvents = [Activator]::CreateInstance($listType)
                $gSlot = [Activator]::CreateInstance($slotType)
                $gSlot.Title = "Hari Raya Natal"
                $gSlot.Start = $futureDay
                $gSlot.End = $futureDay.AddDays(1)
                $gSlot.AllDay = $true
                $gSlot.Source = "google"
                $gSlot.Kind = "jadwal"
                $gEvents.Add($gSlot) | Out-Null

                $islandType.GetField("sources", $flags).SetValue($isle, $hub)
                $islandType.GetField("kuliaClasses", $flags).SetValue($isle, $gEvents)
                $islandType.GetMethod("MergeSources", $flags).Invoke($isle, $null) | Out-Null

                $merged = $islandType.GetField("classes", $flags).GetValue($isle)
                $natalSlots = @($merged | Where-Object { $_.Title -eq "Hari Raya Natal" })
                Assert-Equal $natalSlots.Count 1 "MergeSources should deduplicate holiday slot matching non-holiday (Start.Date, Title)"
                Assert-Equal $natalSlots[0].Source "google" "MergeSources should keep the google slot over the duplicate holiday slot"
                $cutiSlots = @($merged | Where-Object { $_.Title -eq "Cuti Bersama" })
                Assert-Equal $cutiSlots.Count 1 "MergeSources should keep non-duplicate holiday slot"

                $inkType = Get-DeskmateType "Ink"
                $calType = $islandType.GetNestedType("CalendarSheet", $flags)
                $dockType = $islandType.GetNestedType("DockWindow", $flags)
                [void]$inkType.GetMethod("Init", $flags).Invoke($null, $null)
                $ctlActs = $islandType.GetMethod("ControlActs", $flags)
                if ($ctlActs) { [void]$ctlActs.Invoke($isle, $null) }
                $dock = [Activator]::CreateInstance($dockType, @($isle))
                $cal = [Activator]::CreateInstance($calType, @($isle, $dock))
                $bmp = New-Object System.Drawing.Bitmap 360, 800
                $g = [System.Drawing.Graphics]::FromImage($bmp)
                try {
                    $calType.GetField("g", $flags).SetValue($cal, $g)
                    $painter = $calType.GetField("p", $flags).GetValue($cal)
                    $painter.G = $g
                    $painter.T = $inkType.GetField("Base", $flags).GetValue($null)
                    $monRect = (New-Object System.Drawing.Rectangle(0, 0, 1920, 1080)).PSObject.BaseObject
                    $calType.GetMethod("Opening", $flags).Invoke($cal, [object[]]@($monRect, 1000)) | Out-Null
                    $calType.GetProperty("selectedDay", $flags).SetValue($cal, $futureDay.AddDays(1))
                    $calType.GetMethod("Draw", $flags).Invoke($cal, $null) | Out-Null
                    $drawn = $calType.GetMethod("EventsForDay", $flags).Invoke($cal, @($futureDay.AddDays(1)))
                    Assert-Equal $drawn.Count 1 "CalendarSheet should see 1 holiday event for Cuti Bersama day"
                    Assert-Equal $drawn[0].Source "holiday" "CalendarSheet event should have Source == 'holiday'"
                } finally {
                    $calType.GetMethod("Teardown", $flags).Invoke($cal, $null) | Out-Null
                    $g.Dispose()
                    $bmp.Dispose()
                    [System.ComponentModel.Component].GetMethod("Dispose", $flags, $null, [type[]]@(), $null).Invoke($cal, $null) | Out-Null
                    $dock.Dispose()
                }
            } finally {
                $isle.Dispose()
            }
        }

        It "9. Update/Edit Signatures & Lang.T Event Dialog Translations" {
            $slotType = Get-DeskmateType "ClassSlot"
            $strRef = [string].MakeByRefType()
            $expectedParams = @([string], [string], [DateTime], [DateTime], [string], [bool], $strRef)

            $updSlot = $srcType.GetMethod("UpdateEventSlot", $flags, $null, $expectedParams, $null)
            Assert-NotNull $updSlot "GoogleCalendarSource.UpdateEventSlot must have exact 7-parameter signature"
            Assert-Equal $updSlot.ReturnType $slotType "UpdateEventSlot must return ClassSlot"

            $updIsle = $islandType.GetMethod("UpdateGoogleCalendarEvent", $flags, $null, $expectedParams, $null)
            Assert-NotNull $updIsle "Island.UpdateGoogleCalendarEvent must have exact 7-parameter signature"
            Assert-Equal $updIsle.ReturnType ([bool]) "UpdateGoogleCalendarEvent must return bool"

            $openNew = $islandType.GetMethod("OpenNewGoogleEvent", $flags, $null, [type[]]@([DateTime]), $null)
            Assert-NotNull $openNew "Island.OpenNewGoogleEvent(DateTime) must exist"

            $openEdit = $islandType.GetMethod("OpenEditGoogleEvent", $flags, $null, [type[]]@($slotType), $null)
            Assert-NotNull $openEdit "Island.OpenEditGoogleEvent(ClassSlot) must exist"

            $engField = $langType.GetField("English", $flags)
            $prevEng = $engField.GetValue($null)
            try {
                $engField.SetValue($null, $true)
                $tMethod = $langType.GetMethod("T", $flags)
                Assert-Equal ($tMethod.Invoke($null, @("Edit acara"))) "Edit event" "Lang.T should translate 'Edit acara'"
                Assert-Equal ($tMethod.Invoke($null, @("Edit acara…"))) "Edit event…" "Lang.T should translate 'Edit acara…'"
                Assert-Equal ($tMethod.Invoke($null, @("Acara ditambahkan"))) "Event added" "Lang.T should translate 'Acara ditambahkan'"
                Assert-Equal ($tMethod.Invoke($null, @("Acara diperbarui"))) "Event updated" "Lang.T should translate 'Acara diperbarui'"
                Assert-Equal ($tMethod.Invoke($null, @("30 mnt"))) "30 min" "Lang.T should translate '30 mnt'"
                Assert-Equal ($tMethod.Invoke($null, @("+1 Minggu"))) "+1 Week" "Lang.T should translate '+1 Minggu'"
            } finally {
                $engField.SetValue($null, $prevEng)
            }
        }

        It "10. GoogleEventDialog Create Mode: Initial Day, Quick Chips & Zero-Drift Prev/Next" {
            $dlgType = $islandType.GetNestedType("GoogleEventDialog", $flags)
            Assert-NotNull $dlgType "Island+GoogleEventDialog nested type must exist"
            $isle = [Activator]::CreateInstance($islandType)
            try {
                $targetDay = [DateTime]::Today.AddDays(5)
                $ctor = $dlgType.GetConstructor($flags, $null, [type[]]@($islandType, [DateTime]), $null)
                Assert-NotNull $ctor "GoogleEventDialog(Island, DateTime) constructor must exist"
                $dlg = $ctor.Invoke(@($isle, $targetDay))
                try {
                    Assert-False $dlg.IsEdit "Create mode IsEdit should be false"
                    $chosen = $dlgType.GetField("chosen", $flags).GetValue($dlg)
                    Assert-Equal $chosen $targetDay "Initial chosen date should match Today.AddDays(5)"

                    $chipToday = $dlgType.GetField("chipToday", $flags).GetValue($dlg)
                    $chipTomorrow = $dlgType.GetField("chipTomorrow", $flags).GetValue($dlg)
                    $chipWeek = $dlgType.GetField("chipWeek", $flags).GetValue($dlg)
                    $prevBtn = $dlgType.GetField("prevDayBtn", $flags).GetValue($dlg)
                    $nextBtn = $dlgType.GetField("nextDayBtn", $flags).GetValue($dlg)
                    $onClick = [System.Windows.Forms.Control].GetMethod("OnClick", $flags)

                    $onClick.Invoke($chipToday, @([EventArgs]::Empty)) | Out-Null
                    Assert-Equal ($dlgType.GetField("chosen", $flags).GetValue($dlg)) ([DateTime]::Today) "chipToday should set chosen to Today"
                    Assert-True $chipToday.Active "chipToday should be Active"
                    Assert-False $chipTomorrow.Active "chipTomorrow should not be Active"
                    Assert-False $chipWeek.Active "chipWeek should not be Active"

                    $onClick.Invoke($chipTomorrow, @([EventArgs]::Empty)) | Out-Null
                    Assert-Equal ($dlgType.GetField("chosen", $flags).GetValue($dlg)) ([DateTime]::Today.AddDays(1)) "chipTomorrow should set chosen to Today+1"
                    Assert-True $chipTomorrow.Active "chipTomorrow should be Active"

                    $onClick.Invoke($chipWeek, @([EventArgs]::Empty)) | Out-Null
                    Assert-Equal ($dlgType.GetField("chosen", $flags).GetValue($dlg)) ([DateTime]::Today.AddDays(7)) "chipWeek should set chosen to Today+7"
                    Assert-True $chipWeek.Active "chipWeek should be Active"

                    for ($i = 0; $i -lt 3; $i++) { $onClick.Invoke($nextBtn, @([EventArgs]::Empty)) | Out-Null }
                    Assert-Equal ($dlgType.GetField("chosen", $flags).GetValue($dlg)) ([DateTime]::Today.AddDays(10)) "3x nextDayBtn should advance exactly 3 days with zero drift"

                    for ($i = 0; $i -lt 2; $i++) { $onClick.Invoke($prevBtn, @([EventArgs]::Empty)) | Out-Null }
                    Assert-Equal ($dlgType.GetField("chosen", $flags).GetValue($dlg)) ([DateTime]::Today.AddDays(8)) "2x prevDayBtn should step back exactly 2 days with zero drift"
                } finally {
                    $dlg.Dispose()
                }
            } finally {
                $isle.Dispose()
            }
        }

        It "11. GoogleEventDialog Time/Duration Logic, All-Day Toggle & Late-Night Bounds" {
            $dlgType = $islandType.GetNestedType("GoogleEventDialog", $flags)
            $isle = [Activator]::CreateInstance($islandType)
            try {
                $ctor = $dlgType.GetConstructor($flags, $null, [type[]]@($islandType, [DateTime]), $null)
                $dlgToday = $ctor.Invoke(@($isle, [DateTime]::Today))
                try {
                    $sToday = $dlgType.GetField("startMinutes", $flags).GetValue($dlgToday)
                    $eToday = $dlgType.GetField("endMinutes", $flags).GetValue($dlgToday)
                    Assert-True ($eToday -gt $sToday) "Today initialization must never produce endMinutes <= startMinutes ($sToday -> $eToday)"
                    Assert-True ($eToday -le 1439) "endMinutes must stay within 0..1439"
                } finally {
                    $dlgToday.Dispose()
                }

                $dlg = $ctor.Invoke(@($isle, [DateTime]::Today.AddDays(2)))
                try {
                    $setStart = $dlgType.GetMethod("SetStartMinutes", $flags)
                    $applyDur = $dlgType.GetMethod("ApplyDuration", $flags)
                    $allDay = $dlgType.GetField("allDay", $flags).GetValue($dlg)
                    $startBtn = $dlgType.GetField("startBtn", $flags).GetValue($dlg)
                    $endBtn = $dlgType.GetField("endBtn", $flags).GetValue($dlg)
                    $dur30 = $dlgType.GetField("dur30", $flags).GetValue($dlg)
                    $dur60 = $dlgType.GetField("dur60", $flags).GetValue($dlg)
                    $dur120 = $dlgType.GetField("dur120", $flags).GetValue($dlg)

                    # Default future day is 09:00 -> 10:00 (60m)
                    $setStart.Invoke($dlg, @(14 * 60)) | Out-Null
                    Assert-Equal ($dlgType.GetField("startMinutes", $flags).GetValue($dlg)) 840 "SetStartMinutes(840) should set startMinutes=840"
                    Assert-Equal ($dlgType.GetField("endMinutes", $flags).GetValue($dlg)) 900 "SetStartMinutes should preserve 60m duration"

                    $applyDur.Invoke($dlg, @(30)) | Out-Null
                    Assert-Equal ($dlgType.GetField("endMinutes", $flags).GetValue($dlg)) 870 "ApplyDuration(30) should set endMinutes=870"
                    Assert-True $dur30.Active "dur30 should be Active"
                    Assert-False $dur60.Active "dur60 should not be Active"

                    $setStart.Invoke($dlg, @(10 * 60)) | Out-Null
                    Assert-Equal ($dlgType.GetField("endMinutes", $flags).GetValue($dlg)) 630 "SetStartMinutes(600) should preserve 30m duration"

                    $applyDur.Invoke($dlg, @(120)) | Out-Null
                    Assert-Equal ($dlgType.GetField("endMinutes", $flags).GetValue($dlg)) 720 "ApplyDuration(120) should set endMinutes=720"
                    Assert-True $dur120.Active "dur120 should be Active"

                    $applyDur.Invoke($dlg, @(60)) | Out-Null
                    Assert-Equal ($dlgType.GetField("endMinutes", $flags).GetValue($dlg)) 660 "ApplyDuration(60) should set endMinutes=660"
                    Assert-True $dur60.Active "dur60 should be Active"

                    # Late-night boundary: SetStartMinutes(23:45) must keep endMinutes > startMinutes and <= 1439
                    $setStart.Invoke($dlg, @(23 * 60 + 45)) | Out-Null
                    $lateStart = $dlgType.GetField("startMinutes", $flags).GetValue($dlg)
                    $lateEnd = $dlgType.GetField("endMinutes", $flags).GetValue($dlg)
                    Assert-True ($lateEnd -gt $lateStart) "Late-night SetStartMinutes must keep endMinutes > startMinutes ($lateStart -> $lateEnd)"
                    Assert-True ($lateEnd -le 1439) "Late-night endMinutes must be <= 1439"

                    # All-day toggle disables time/duration controls
                    $allDay.Checked = $true
                    Assert-False $startBtn.Enabled "startBtn must be disabled when allDay is checked"
                    Assert-False $endBtn.Enabled "endBtn must be disabled when allDay is checked"
                    Assert-False $dur30.Enabled "dur30 must be disabled when allDay is checked"
                    Assert-False $dur60.Enabled "dur60 must be disabled when allDay is checked"
                    Assert-False $dur120.Enabled "dur120 must be disabled when allDay is checked"
                } finally {
                    $dlg.Dispose()
                }
            } finally {
                $isle.Dispose()
            }
        }

        It "12. GoogleEventDialog Edit Mode, Non-Overlapping Relayout & Popup Controls Paint" {
            $dlgType = $islandType.GetNestedType("GoogleEventDialog", $flags)
            $calPopType = $dlgType.GetNestedType("CalendarPopupControl", $flags)
            $timePopType = $dlgType.GetNestedType("TimePopupControl", $flags)
            $slotType = Get-DeskmateType "ClassSlot"
            $kitInkType = Get-DeskmateType "KitInk"
            $inkType = Get-DeskmateType "Ink"
            [void]$inkType.GetMethod("Init", $flags).Invoke($null, $null)

            $isle = [Activator]::CreateInstance($islandType)
            try {
                $evDay = [DateTime]::Today.AddDays(3)
                $slot = [Activator]::CreateInstance($slotType)
                $slot.Id = "evt-edit-123"
                $slot.Title = "Sprint Review"
                $slot.Room = "Room 402"
                $slot.Start = $evDay.AddHours(13).AddMinutes(30)
                $slot.End = $evDay.AddHours(15).AddMinutes(0)
                $slot.AllDay = $false

                $ctor = $dlgType.GetConstructor($flags, $null, [type[]]@($islandType, $slotType), $null)
                Assert-NotNull $ctor "GoogleEventDialog(Island, ClassSlot) constructor must exist"
                $dlg = $ctor.Invoke(@($isle, $slot))
                try {
                    Assert-True $dlg.IsEdit "Edit mode IsEdit should be true"
                    Assert-Equal $dlg.EventId "evt-edit-123" "EventId should match ClassSlot.Id"
                    $titleField = $dlgType.GetField("titleField", $flags).GetValue($dlg)
                    $locField = $dlgType.GetField("locationField", $flags).GetValue($dlg)
                    $deleteBtn = $dlgType.GetField("deleteBtn", $flags).GetValue($dlg)
                    Assert-Equal $titleField.Box.Text "Sprint Review" "Edit mode should pre-populate title"
                    Assert-Equal $locField.Box.Text "Room 402" "Edit mode should pre-populate location"
                    Assert-Equal ($dlgType.GetField("chosen", $flags).GetValue($dlg)) $evDay "Edit mode should pre-populate date"
                    Assert-Equal ($dlgType.GetField("startMinutes", $flags).GetValue($dlg)) (13 * 60 + 30) "Edit mode should pre-populate startMinutes"
                    Assert-Equal ($dlgType.GetField("endMinutes", $flags).GetValue($dlg)) (15 * 60) "Edit mode should pre-populate endMinutes"

                    $dlg.ShowInTaskbar = $false
                    $dlg.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
                    $dlg.Location = New-Object System.Drawing.Point(-32000, -32000)
                    $dlg.Show()
                    [System.Windows.Forms.Application]::DoEvents()
                    Assert-True $dlg.IsHandleCreated "Dialog handle should be created on Show()"
                    Assert-True $deleteBtn.Visible "deleteBtn.Visible must be true in Edit mode"

                    $dlgType.GetMethod("Relayout", $flags).Invoke($dlg, $null) | Out-Null
                    $cs = $dlg.ClientSize
                    $clientRect = New-Object System.Drawing.Rectangle(0, 0, $cs.Width, $cs.Height)
                    $vis = @($dlg.Controls | Where-Object { $_.Visible })
                    Assert-True ($vis.Count -ge 15) "Expected at least 15 visible controls in Edit mode"
                    for ($i = 0; $i -lt $vis.Count; $i++) {
                        $b = $vis[$i].Bounds
                        Assert-True ($clientRect.Contains($b)) "Control '$($vis[$i].Name)' ($b) must fit inside ClientSize ($clientRect)"
                        for ($j = $i + 1; $j -lt $vis.Count; $j++) {
                            $b2 = $vis[$j].Bounds
                            $inter = [System.Drawing.Rectangle]::Intersect($b, $b2)
                            Assert-True ($inter.Width -le 0 -or $inter.Height -le 0) "Controls $i ($b) and $j ($b2) must not overlap (overlap: $inter)"
                        }
                    }
                } finally {
                    $dlg.Close()
                    $dlg.Dispose()
                }

                # CalendarPopupControl + TimePopupControl paint visible non-background pixels
                $winBg = $kitInkType.GetProperty("Window", $flags).GetValue($null)
                $onPaintCal = $calPopType.GetMethod("OnPaint", $flags)
                $calPop = [Activator]::CreateInstance($calPopType, @([DateTime]::Today, [float]1.0, [Action[DateTime]]{ param($d) }))
                try {
                    $bmp = New-Object System.Drawing.Bitmap($calPop.Width, $calPop.Height)
                    $g = [System.Drawing.Graphics]::FromImage($bmp)
                    try {
                        $pe = (New-Object System.Windows.Forms.PaintEventArgs($g, (New-Object System.Drawing.Rectangle(0, 0, $calPop.Width, $calPop.Height)))).PSObject.BaseObject
                        $onPaintCal.Invoke($calPop, [object[]]@($pe)) | Out-Null
                        $nonBg = 0
                        for ($y = 0; $y -lt $bmp.Height; $y += 4) {
                            for ($x = 0; $x -lt $bmp.Width; $x += 4) {
                                if ($bmp.GetPixel($x, $y).ToArgb() -ne $winBg.ToArgb()) { $nonBg++ }
                            }
                        }
                        Assert-True ($nonBg -gt 20) "CalendarPopupControl should paint visible non-background pixels (got $nonBg)"
                    } finally {
                        $g.Dispose()
                        $bmp.Dispose()
                    }
                } finally {
                    $calPop.Dispose()
                }

                $onPaintTime = $timePopType.GetMethod("OnPaint", $flags)
                $timePop = [Activator]::CreateInstance($timePopType, @((9 * 60), (8 * 60), "Waktu selesai", [float]1.0, [Action[int]]{ param($m) }))
                try {
                    $bmp = New-Object System.Drawing.Bitmap($timePop.Width, $timePop.Height)
                    $g = [System.Drawing.Graphics]::FromImage($bmp)
                    try {
                        $pe = (New-Object System.Windows.Forms.PaintEventArgs($g, (New-Object System.Drawing.Rectangle(0, 0, $timePop.Width, $timePop.Height)))).PSObject.BaseObject
                        $onPaintTime.Invoke($timePop, [object[]]@($pe)) | Out-Null
                        $nonBg = 0
                        for ($y = 0; $y -lt $bmp.Height; $y += 4) {
                            for ($x = 0; $x -lt $bmp.Width; $x += 4) {
                                if ($bmp.GetPixel($x, $y).ToArgb() -ne $winBg.ToArgb()) { $nonBg++ }
                            }
                        }
                        Assert-True ($nonBg -gt 20) "TimePopupControl should paint visible non-background pixels (got $nonBg)"
                    } finally {
                        $g.Dispose()
                        $bmp.Dispose()
                    }
                } finally {
                    $timePop.Dispose()
                }
            } finally {
                $isle.Dispose()
            }
        }
    } finally {
        if ($hadVault -and $null -ne $backupVault) {
            [System.IO.File]::WriteAllBytes($vaultFile, $backupVault)
        } elseif (-not $hadVault -and (Test-Path $vaultFile)) {
            Remove-Item $vaultFile -Force
        }
    }
}
