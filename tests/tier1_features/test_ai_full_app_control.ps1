$commonDir = Join-Path $PSScriptRoot '..\common'
. (Join-Path $commonDir 'TestHarness.ps1')
. (Join-Path $commonDir 'DeskmateLoader.ps1')

Describe 'AI full-app management tools and parsing' {
    $islandType = Get-DeskmateType 'Island'
    $aiCliType = Get-DeskmateType 'AiCli'
    $staticFlags = [Reflection.BindingFlags]'Static,NonPublic'
    $instFlags = [Reflection.BindingFlags]'Instance,NonPublic'

    $buildTools = $islandType.GetMethod('BuildSuruhTools', $staticFlags)
    $argBool = $islandType.GetMethod('ArgBool', $staticFlags)
    $argInt = $islandType.GetMethod('ArgInt', $staticFlags)
    $tryParseDt = $islandType.GetMethod('TryParseAskDateTime', $staticFlags)
    $toolGuide = $aiCliType.GetMethod('ToolGuide', $staticFlags)
    $readShape = $aiCliType.GetMethod('ReadShape', $staticFlags)

    It 'Exposes all full-app management tools in BuildSuruhTools' {
        $tools = $buildTools.Invoke($null, @($false))
        $names = @()
        foreach ($t in $tools) {
            $names += $t['function']['name']
        }
        $expected = @(
            'read_deskmate', 'manage_task', 'manage_note', 'manage_calendar',
            'manage_reminder', 'manage_timer', 'manage_media', 'manage_system',
            'manage_shelf_clipboard', 'manage_integrations', 'manage_tools',
            'manage_settings', 'open_deskmate', 'reply'
        )
        foreach ($name in $expected) {
            Assert-True ($names -contains $name) "Missing tool: $name"
        }
        Assert-False ($names -contains 'add_task')

        $toolsWithExt = $buildTools.Invoke($null, @($true))
        $extNames = @()
        foreach ($t in $toolsWithExt) { $extNames += $t['function']['name'] }
        Assert-True ($extNames -contains 'add_task')
    }

    It 'Generates valid CLI ToolGuide and parses multi-tool ReadShape responses' {
        $tools = $buildTools.Invoke($null, @($false))
        $guide = $toolGuide.Invoke($null, @(,$tools))
        Assert-True ($guide.Contains('manage_task'))
        Assert-True ($guide.Contains('manage_note'))
        Assert-True ($guide.Contains('manage_calendar'))
        Assert-True ($guide.Contains('manage_system'))

        $allowed = New-Object 'System.Collections.Generic.List[string]'
        foreach ($t in $tools) { $allowed.Add($t['function']['name']) }

        $json = '{"text":"Siap!","calls":[{"name":"manage_task","arguments":"{\"action\":\"add\",\"title\":\"Beli susu\",\"list\":\"Belanja\"}"},{"name":"manage_note","arguments":"{\"action\":\"create\",\"title\":\"Belanja\",\"content\":\"- Susu\"}"}]}'
        $reply = $readShape.Invoke($null, [object[]]@($json, $allowed.PSObject.BaseObject))
        Assert-Equal $reply.Text 'Siap!'
        Assert-Equal $reply.Calls.Count 2
        Assert-Equal $reply.Calls[0].Key 'manage_task'
        Assert-Equal $reply.Calls[0].Value['title'] 'Beli susu'
        Assert-Equal $reply.Calls[1].Key 'manage_note'
        Assert-Equal $reply.Calls[1].Value['title'] 'Belanja'
    }

    It 'Parses boolean, integer, and local date/datetime arguments reliably' {
        $d = New-Object 'System.Collections.Generic.Dictionary[string,object]'
        $d['a'] = $true
        $d['b'] = 'ya'
        $d['c'] = 'off'
        $d['d'] = 'tidak'
        $d['n1'] = 42
        $d['n2'] = '75'
        $d['n3'] = 12.6

        $rawD = $d.PSObject.BaseObject
        Assert-Equal ($argBool.Invoke($null, [object[]]@($rawD, 'a'))) $true
        Assert-Equal ($argBool.Invoke($null, [object[]]@($rawD, 'b'))) $true
        Assert-Equal ($argBool.Invoke($null, [object[]]@($rawD, 'c'))) $false
        Assert-Equal ($argBool.Invoke($null, [object[]]@($rawD, 'd'))) $false
        Assert-True ($null -eq $argBool.Invoke($null, [object[]]@($rawD, 'missing')))

        Assert-Equal ($argInt.Invoke($null, [object[]]@($rawD, 'n1'))) 42
        Assert-Equal ($argInt.Invoke($null, [object[]]@($rawD, 'n2'))) 75
        Assert-Equal ($argInt.Invoke($null, [object[]]@($rawD, 'n3'))) 13

        $params1 = @('2026-11-26T14:30', [datetime]::MinValue, $false)
        $ok1 = $tryParseDt.Invoke($null, $params1)
        Assert-True $ok1
        Assert-True $params1[2]
        Assert-Equal $params1[1].Hour 14
        Assert-Equal $params1[1].Minute 30

        $params2 = @('2026-11-26', [datetime]::MinValue, $false)
        $ok2 = $tryParseDt.Invoke($null, $params2)
        Assert-True $ok2
        Assert-False $params2[2]
        Assert-Equal $params2[1].Day 26

        $params3 = @('not-a-date', [datetime]::MinValue, $false)
        $ok3 = $tryParseDt.Invoke($null, $params3)
        Assert-False $ok3
    }

    It 'Routes Google Calendar mutations to worker thread and UI actions to UI thread' {
        $isWorker = $islandType.GetMethod('IsWorkerTool', $instFlags)
        $inst = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($islandType)

        $calCreate = New-Object 'System.Collections.Generic.Dictionary[string,object]'
        $calCreate['action'] = 'create'
        Assert-True ($isWorker.Invoke($inst, [object[]]@('manage_calendar', $calCreate.PSObject.BaseObject)))

        $calUpdate = New-Object 'System.Collections.Generic.Dictionary[string,object]'
        $calUpdate['action'] = 'update'
        Assert-True ($isWorker.Invoke($inst, [object[]]@('manage_calendar', $calUpdate.PSObject.BaseObject)))

        $calDelete = New-Object 'System.Collections.Generic.Dictionary[string,object]'
        $calDelete['action'] = 'delete'
        Assert-True ($isWorker.Invoke($inst, [object[]]@('manage_calendar', $calDelete.PSObject.BaseObject)))

        $calLink = New-Object 'System.Collections.Generic.Dictionary[string,object]'
        $calLink['action'] = 'open_link'
        Assert-False ($isWorker.Invoke($inst, [object[]]@('manage_calendar', $calLink.PSObject.BaseObject)))

        $taskAdd = New-Object 'System.Collections.Generic.Dictionary[string,object]'
        $taskAdd['action'] = 'add'
        Assert-False ($isWorker.Invoke($inst, [object[]]@('manage_task', $taskAdd.PSObject.BaseObject)))
    }
}
