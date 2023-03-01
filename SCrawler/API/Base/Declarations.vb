﻿' Copyright (C) 2023  Andy https://github.com/AAndyProgram
' This program is free software: you can redistribute it and/or modify
' it under the terms of the GNU General Public License as published by
' the Free Software Foundation, either version 3 of the License, or
' (at your option) any later version.
'
' This program is distributed in the hope that it will be useful,
' but WITHOUT ANY WARRANTY
Namespace API.Base
    Friend Module Declarations
        Friend ReadOnly LNC As New ListAddParams(LAP.NotContainsOnly)
        Friend ReadOnly TitleHtmlConverter As Func(Of String, String) =
               Function(Input) SymbolsConverter.HTML.Decode(SymbolsConverter.Convert(Input, EDP.ReturnValue), EDP.ReturnValue).
                               StringRemoveWinForbiddenSymbols().StringTrim()
    End Module
End Namespace