#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Adds .ConfigureAwait(true) to all await expressions in test .cs files.

.DESCRIPTION
    Processes all .cs files under d:\w1\tests\ and:
    1. Replaces .ConfigureAwait(false) with .ConfigureAwait(true)
    2. Adds .ConfigureAwait(true) to await expressions that don't have it

    Strategy:
    - Single-line await: use parenthesis balancing to find the exact end of the
      awaited expression, then insert .ConfigureAwait(true) after it
    - Multi-line await: use parenthesis balancing across lines to find the
      true end of the await expression
    - Skips await in comments, await using/foreach

    Skips: Testing.Common, MockServers directories, auto-generated files (.Designer.cs, .g.cs, .g.i.cs)

.EXAMPLE
    .\add-configureawait-true.ps1
#>

$ErrorActionPreference = 'Stop'
$testDir = 'd:\w1\tests'
$skipDirs = @('Testing.Common', 'MockServers')
$skipSuffixes = @('.Designer.cs', '.g.cs', '.g.i.cs')

$totalFilesModified = 0
$totalFalseToTrue = 0
$totalAdded = 0

# Find all .cs files
$files = @(Get-ChildItem -Path $testDir -Filter '*.cs' -Recurse -File)
Write-Host "Found $($files.Count) .cs files under $testDir"

function FindAwaitExpressionEnd([string]$line, [int]$awaitStart) {
    # Given a line and the start position of 'await', find the position
    # where the awaited expression ends (the closing paren of the LAST method call
    # in a potential chain like ReadLineAsync().WaitAsync(...)).
    # Uses parenthesis balancing, skipping strings and comments.
    # Returns the index of the closing ')' of the last method call,
    # or -1 if not found on this line.

    $depth = 0
    $foundOpenParen = $false
    $lastBalancedPos = -1
    $inString = $false
    $stringChar = [char]0
    $i = $awaitStart

    while ($i -lt $line.Length) {
        $ch = $line[$i]
        $nextCh = if ($i + 1 -lt $line.Length) { $line[$i + 1] } else { [char]0 }

        if ($inString) {
            if ($ch -eq '\' -and $nextCh -ne [char]0) {
                $i += 2  # skip escape sequence
                continue
            }
            if ($ch -eq $stringChar) {
                $inString = $false
            }
        } else {
            if ($ch -eq '"') {
                $inString = $true
                $stringChar = '"'
            } elseif ($ch -eq "'") {
                # Could be char literal or string - treat as string for safety
                $inString = $true
                $stringChar = "'"
            } elseif ($ch -eq '/' -and $nextCh -eq '/') {
                break  # rest is comment
            } elseif ($ch -eq '(') {
                $depth++
                $foundOpenParen = $true
            } elseif ($ch -eq ')') {
                $depth--
                if ($foundOpenParen -and $depth -eq 0) {
                    # Found a balanced closing paren
                    # Check if followed by .MethodName( (chain call)
                    $lastBalancedPos = $i
                    # Look ahead for chained method call: .Word(
                    $lookAhead = $i + 1
                    # Skip whitespace
                    while ($lookAhead -lt $line.Length -and $line[$lookAhead] -eq ' ') { $lookAhead++ }
                    if ($lookAhead -lt $line.Length -and $line[$lookAhead] -eq '.') {
                        # Potential chain - continue scanning
                        # Don't return yet, keep looking for more balanced parens
                    } else {
                        # No chain - this is the end
                        return $lastBalancedPos
                    }
                }
            }
        }
        $i++
    }
    # Return the last balanced position if we found one (end of chain)
    return $lastBalancedPos
}

function CountParens([string]$text) {
    # Count net unbalanced ( over ) ignoring strings and comments
    $depth = 0
    $inString = $false
    $stringChar = [char]0
    $i = 0
    while ($i -lt $text.Length) {
        $ch = $text[$i]
        $nextCh = if ($i + 1 -lt $text.Length) { $text[$i + 1] } else { [char]0 }

        if ($inString) {
            if ($ch -eq '\' -and $nextCh -ne [char]0) {
                $i += 2
                continue
            }
            if ($ch -eq $stringChar) {
                $inString = $false
            }
        } else {
            if ($ch -eq '"') {
                $inString = $true
                $stringChar = '"'
            } elseif ($ch -eq "'") {
                $inString = $true
                $stringChar = "'"
            } elseif ($ch -eq '/' -and $nextCh -eq '/') {
                break
            } elseif ($ch -eq '(') {
                $depth++
            } elseif ($ch -eq ')') {
                $depth--
            }
        }
        $i++
    }
    return $depth
}

foreach ($file in $files) {
    # Skip auto-generated files
    $skipFile = $false
    foreach ($suffix in $skipSuffixes) {
        if ($file.Name.EndsWith($suffix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $skipFile = $true
            break
        }
    }
    if ($skipFile) { continue }

    # Skip files in excluded directories
    $relativePath = $file.FullName.Substring($testDir.Length + 1)
    foreach ($skipDir in $skipDirs) {
        if ($relativePath.StartsWith($skipDir + '\', [System.StringComparison]::OrdinalIgnoreCase) -or
            $relativePath.StartsWith($skipDir + '/', [System.StringComparison]::OrdinalIgnoreCase)) {
            $skipFile = $true
            break
        }
    }
    if ($skipFile) { continue }

    # Read file content
    $content = [System.IO.File]::ReadAllText($file.FullName)
    $originalContent = $content
    $fileFalseToTrue = 0
    $fileAdded = 0

    # Step 1: Replace .ConfigureAwait(false) with .ConfigureAwait(true)
    $falseMatches = [regex]::Matches($content, '\.ConfigureAwait\(false\)')
    $fileFalseToTrue = $falseMatches.Count
    if ($fileFalseToTrue -gt 0) {
        $content = $content.Replace('.ConfigureAwait(false)', '.ConfigureAwait(true)')
    }

    # Step 2: Add .ConfigureAwait(true) to await expressions without it
    $lineEnding = if ($originalContent.Contains("`r`n")) { "`r`n" } else { "`n" }
    $lines = $content -split "`r?`n"
    $newLines = [System.Collections.Generic.List[string]]::new($lines.Count)

    # Track pending multi-line await
    $pendingAwait = $false
    $parenDepth = 0

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $trimmedLine = $line.TrimStart()

        # If we have a pending multi-line await, track parenthesis balance
        if ($pendingAwait) {
            $lineDepth = CountParens $line
            $newDepth = $parenDepth + $lineDepth

            # Check if this line ends the await expression
            # Condition: parens are balanced (depth = 0) AND line has ;
            $isEndOfAwait = $false
            if ($newDepth -eq 0 -and $line -match ';') {
                $isEndOfAwait = $true
            }
            # Also check for chain continuation: if depth is 0 but no ;,
            # and next line starts with ., keep tracking
            if ($newDepth -eq 0 -and -not ($line -match ';')) {
                # Check if next line continues the chain
                if (($i + 1) -lt $lines.Count) {
                    $nextTrimmed = $lines[$i + 1].TrimStart()
                    if ($nextTrimmed.StartsWith('.')) {
                        # Chain continues - keep pending
                        $parenDepth = $newDepth
                        $newLines.Add($line)
                        continue
                    }
                }
                # No chain continuation and no ; - end of await?
                # This could be a ternary or other construct - cancel pending
                $pendingAwait = $false
                $parenDepth = 0
                $newLines.Add($line)
                continue
            }

            if ($isEndOfAwait) {
                # All parens balanced and we found the semicolon
                if ($line -notmatch 'ConfigureAwait') {
                    # Find the last ) before ; and insert after it
                    $newLine = $line -replace '(\))(\s*;)', '$1.ConfigureAwait(true)$2'
                    if ($newLine -ne $line) {
                        $fileAdded++
                    }
                    $newLines.Add($newLine)
                } else {
                    $newLines.Add($line)
                }
                $pendingAwait = $false
                $parenDepth = 0
                continue
            }

            # Update cumulative paren depth
            $parenDepth = $newDepth

            # If parens went negative, something is wrong - cancel pending
            if ($parenDepth -lt 0) {
                $pendingAwait = $false
                $parenDepth = 0
            }

            $newLines.Add($line)
            continue
        }

        # Skip pure comment lines
        if ($trimmedLine.StartsWith('//') -or $trimmedLine.StartsWith('/*') -or $trimmedLine.StartsWith('*')) {
            $newLines.Add($line)
            continue
        }

        # Skip lines without 'await' keyword
        if (-not ($line -match '\bawait\b')) {
            $newLines.Add($line)
            continue
        }

        # Skip 'await using' and 'await foreach'
        if ($line -match '\bawait\s+(using|foreach)\b') {
            $newLines.Add($line)
            continue
        }

        # Skip lines that already have ConfigureAwait
        if ($line -match 'ConfigureAwait') {
            $newLines.Add($line)
            continue
        }

        # Check if 'await' is inside a line comment
        $commentIdx = $line.IndexOf('//')
        $awaitIdx = $line.IndexOf('await')
        if ($commentIdx -ge 0 -and $awaitIdx -ge 0 -and $commentIdx -lt $awaitIdx) {
            $newLines.Add($line)
            continue
        }

        # This line has an await without ConfigureAwait
        # Use parenthesis balancing to find the exact end of the awaited expression
        $endPos = FindAwaitExpressionEnd $line $awaitIdx

        if ($endPos -ge 0) {
            # Found the closing ) of the awaited method call on this line
            # Check if there's a chain call on the next line (starts with . after whitespace)
            $afterEnd = $line.Substring($endPos + 1).Trim()
            $nextLineStartsWithDot = $false
            if ($afterEnd -eq '' -and ($i + 1) -lt $lines.Count) {
                $nextTrimmed = $lines[$i + 1].TrimStart()
                if ($nextTrimmed.StartsWith('.')) {
                    $nextLineStartsWithDot = $true
                }
            }

            if ($nextLineStartsWithDot) {
                # Chain call continues on next line - use multi-line mode
                # The paren depth is 0 at this point, but we need to track
                # the chain continuation. Set pendingAwait with parenDepth = 0
                # and look for the line that ends with ;
                $parenDepth = 0
                $pendingAwait = $true
                $newLines.Add($line)
            } else {
                # No chain - insert .ConfigureAwait(true) after the closing )
                $newLine = $line.Substring(0, $endPos + 1) + '.ConfigureAwait(true)' + $line.Substring($endPos + 1)
                $fileAdded++
                $newLines.Add($newLine)
            }
        } elseif ($line -match ';') {
            # Has ; but couldn't find balanced parens (e.g., await without method call parens)
            # Fallback: add before ;
            $newLine = $line -replace '(await\s+.+?)(\s*;)', '$1.ConfigureAwait(true)$2'
            if ($newLine -ne $line) {
                $fileAdded++
            }
            $newLines.Add($newLine)
        } else {
            # Multi-line case: calculate paren depth from this line
            $parenDepth = CountParens $line
            if ($parenDepth -gt 0) {
                $pendingAwait = $true
            }
            $newLines.Add($line)
        }
    }

    $content = $newLines -join $lineEnding

    if ($content -ne $originalContent) {
        # Write without BOM (UTF-8 No BOM)
        $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
        [System.IO.File]::WriteAllText($file.FullName, $content, $utf8NoBom)
        $totalFilesModified++
        $totalFalseToTrue += $fileFalseToTrue
        $totalAdded += $fileAdded
        Write-Host "  Modified: $($file.FullName) (false->true: $fileFalseToTrue, added: $fileAdded)"
    }
}

Write-Host ''
Write-Host '=== Summary ==='
Write-Host "Total files modified: $totalFilesModified"
Write-Host "  .ConfigureAwait(false) -> .ConfigureAwait(true): $totalFalseToTrue"
Write-Host "  Added .ConfigureAwait(true): $totalAdded"
Write-Host "  Total replacements: $($totalFalseToTrue + $totalAdded)"
