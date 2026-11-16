$commonDir = Join-Path $PSScriptRoot '..\common'
. (Join-Path $commonDir 'TestHarness.ps1')
. (Join-Path $commonDir 'DeskmateLoader.ps1')

Describe 'Ask Deskmate provider compatibility' {
    $ai = Get-DeskmateType 'Ai'
    $flags = [Reflection.BindingFlags]'Static,NonPublic'
    $read = $ai.GetMethod('ReadChatCompletion', $flags)

    It 'Reads a standard chat completion and structured text blocks' {
        $plain = $read.Invoke($null, @('{"choices":[{"message":{"content":"Halo!"}}]}'))
        $parts = $read.Invoke($null, @('{"choices":[{"message":{"content":[{"type":"text","text":"Halo!"},{"type":"text","text":{"value":"Ada yang bisa dibantu?"}}]}}]}'))
        Assert-Equal $plain.Text 'Halo!'
        Assert-Equal $parts.Text "Halo!`nAda yang bisa dibantu?"
    }

    It 'Reads a text event stream from a compatible server' {
        $stream = @(
            'data: {"choices":[{"delta":{"content":"Ha"}}]}'
            'data: {"choices":[{"delta":{"content":"lo!"}}]}'
            'data: [DONE]'
        ) -join "`n`n"
        Assert-Equal ($read.Invoke($null, @($stream))).Text 'Halo!'
    }

    It 'Reads object and encoded tool arguments' {
        $obj = $read.Invoke($null, @('{"choices":[{"message":{"tool_calls":[{"function":{"name":"open_deskmate","arguments":{"section":"tasks"}}}]}}]}'))
        $encoded = $read.Invoke($null, @('{"choices":[{"message":{"tool_calls":[{"function":{"name":"read_deskmate","arguments":"{\"section\":\"schedule\"}"}}]}}]}'))
        Assert-Equal $obj.Calls[0].Value['section'] 'tasks'
        Assert-Equal $encoded.Calls[0].Value['section'] 'schedule'
    }

    It 'Rejects empty, malformed and non-API answers' {
        foreach ($body in @('{"choices":[]}', '{"choices":[{"message":{"content":" "}}]}', '<html>Wrong endpoint</html>', '{')) {
            Assert-True ($null -eq $read.Invoke($null, @($body))) "Expected empty result for $body"
        }
    }

    It 'Allows loopback HTTP and rejects deceptive host prefixes' {
        $validate = $ai.GetMethod('ValidCustomUrl', $flags)
        Assert-True ($validate.Invoke($null, @('http://127.0.0.1:8080/v1/chat/completions')))
        Assert-True ($validate.Invoke($null, @('https://example.com/v1')))
        Assert-False ($validate.Invoke($null, @('http://localhost.example.com/v1')))
        Assert-False ($validate.Invoke($null, @('http://localhost@example.com/v1')))
    }
}
