﻿' Copyright (C) 2023  Andy https://github.com/AAndyProgram
' This program is free software: you can redistribute it and/or modify
' it under the terms of the GNU General Public License as published by
' the Free Software Foundation, either version 3 of the License, or
' (at your option) any later version.
'
' This program is distributed in the hope that it will be useful,
' but WITHOUT ANY WARRANTY
Imports System.Threading
Imports SCrawler.API.Base
Imports SCrawler.API.YouTube.Objects
Imports PersonalUtilities.Functions.XML
Imports PersonalUtilities.Functions.RegularExpressions
Imports PersonalUtilities.Tools.Web.Documents.JSON
Imports UTypes = SCrawler.API.Base.UserMedia.Types
Namespace API.Mastodon
    Friend Class UserData : Inherits Twitter.UserData
#Region "XML names"
        Private Const Name_UserDomain As String = "UserDomain"
        Private Const Name_TrueName As String = "TrueName"
#End Region
#Region "Declarations"
        Private _UserDomain As String = String.Empty
        Friend Property UserDomain As String
            Get
                Return _UserDomain.IfNullOrEmpty(MySettings.MyDomain.Value)
            End Get
            Set(ByVal d As String)
                _UserDomain = d
            End Set
        End Property
        Friend Property TrueName As String = String.Empty
        Private ReadOnly Property MySettings As SiteSettings
            Get
                Return HOST.Source
            End Get
        End Property
        Private MyCredentials As Credentials
        Private Sub ResetCredentials()
            MyCredentials = Nothing
            With MySettings
                Dim setDef As Boolean = True
                If Not IsSavedPosts Then
                    If ACheck(.MyDomain.Value) AndAlso UserDomain = .MyDomain.Value Then
                        setDef = True
                    ElseIf .Domains.Credentials.Count > 0 Then
                        Dim i% = .Domains.Credentials.IndexOf(UserDomain)
                        If i >= 0 Then
                            MyCredentials = .Domains.Credentials(i)
                            setDef = Not MyCredentials.Exists
                        End If
                    End If
                End If
                If setDef Then MyCredentials = New Credentials With {.Domain = UserDomain, .Bearer = MySettings.Auth.Value, .Csrf = MySettings.Token.Value}
            End With
            With MyCredentials
                Responser.Headers.Add(Twitter.SiteSettings.Header_Authorization, .Bearer)
                Responser.Headers.Add(Twitter.SiteSettings.Header_Token, .Csrf)
            End With
        End Sub
#End Region
#Region "LoadUserInformation"
        Protected Overrides Sub LoadUserInformation_OptionalFields(ByRef Container As XmlFile, ByVal Loading As Boolean)
            MyBase.LoadUserInformation_OptionalFields(Container, Loading)
            Dim obtainNames As Action = Sub()
                                            If _UserDomain.IsEmptyString And Not Name.IsEmptyString Then
                                                Dim l$() = Name.Split("@")
                                                If l.ListExists(2) Then
                                                    _UserDomain = l(0)
                                                    TrueName = l(1)
                                                Else
                                                    _UserDomain = MySettings.MyDomain.Value
                                                    TrueName = Name
                                                End If
                                                If FriendlyName.IsEmptyString Then FriendlyName = TrueName
                                            End If
                                        End Sub
            If Loading Then
                _UserDomain = Container.Value(Name_UserDomain)
                TrueName = Container.Value(Name_TrueName)
                obtainNames.Invoke
            Else
                obtainNames.Invoke
                Container.Add(Name_UserDomain, _UserDomain)
                Container.Add(Name_TrueName, TrueName)
                Container.Value(Name_FriendlyName) = FriendlyName
            End If
        End Sub
#End Region
#Region "Download functions"
        Protected Overrides Sub DownloadDataF(ByVal Token As CancellationToken)
            ResetCredentials()
            DownloadData(String.Empty, Token)
        End Sub
        Private Overloads Sub DownloadData(ByVal POST As String, ByVal Token As CancellationToken)
            Dim URL$ = String.Empty
            Try
                Dim PostID$ = String.Empty
                Dim PostDate$
                Dim s As EContainer, ss As EContainer
                Dim NewPostDetected As Boolean = False
                Dim ExistsDetected As Boolean = False

                If IsSavedPosts Then
                    URL = $"https://{MySettings.MyDomain.Value}/api/v1/bookmarks"
                    If Not POST.IsEmptyString Then URL &= $"?max_id={POST}"
                Else
                    If POST.IsEmptyString And ID.IsEmptyString Then
                        ObtainUserID()
                        If ID.IsEmptyString Then Throw New ArgumentNullException("ID", "Unable to get user ID") With {.HelpLink = 1}
                    End If
                    URL = $"https://{MyCredentials.Domain}/api/v1/accounts/{ID}/statuses?"
                    If ParseUserMediaOnly Then URL &= "only_media=true&"
                    URL &= "limit=40"
                    If Not POST.IsEmptyString Then URL &= $"&max_id={POST}"
                End If

                ThrowAny(Token)
                Dim r$ = Responser.GetResponse(URL)
                If Not r.IsEmptyString Then
                    Using j As EContainer = JsonDocument.Parse(r)
                        If If(j?.Count, 0) > 0 Then
                            ProgressPre.ChangeMax(j.Count)
                            For Each jj As EContainer In j
                                ProgressPre.Perform()
                                With jj
                                    If Not IsSavedPosts And POST.IsEmptyString And Not .Item("account") Is Nothing Then
                                        With .Item("account")
                                            If .Value("id") = ID Then
                                                UserSiteNameUpdate(.Value("display_name"))
                                                UserDescriptionUpdate(.Value("note"))
                                                Dim __getImage As Action(Of String) = Sub(ByVal img As String)
                                                                                          If Not img.IsEmptyString Then
                                                                                              Dim __imgFile As SFile = img
                                                                                              If Not __imgFile.Name.IsEmptyString Then
                                                                                                  If __imgFile.Extension.IsEmptyString Then __imgFile.Extension = "jpg"
                                                                                                  __imgFile.Path = MyFile.CutPath.Path
                                                                                                  If Not __imgFile.Exists Then GetWebFile(img, __imgFile, EDP.None)
                                                                                              End If
                                                                                          End If
                                                                                      End Sub
                                                __getImage.Invoke(.Value("header").IfNullOrEmpty(.Value("header_static")))
                                                __getImage.Invoke(.Value("avatar").IfNullOrEmpty(.Value("avatar_static")))
                                            End If
                                        End With
                                    End If

                                    PostID = .Value("id")
                                    PostDate = .Value("created_at")

                                    If Not IsSavedPosts And Not PostDate.IsEmptyString Then
                                        Select Case CheckDatesLimit(PostDate, DateProvider)
                                            Case DateResult.Skip : Continue For
                                            Case DateResult.Exit : Exit Sub
                                        End Select
                                    End If

                                    If Not _TempPostsList.Contains(PostID) Then
                                        NewPostDetected = True
                                        _TempPostsList.Add(PostID)
                                    Else
                                        ExistsDetected = True
                                        Continue For
                                    End If

                                    If IsSavedPosts OrElse (Not ParseUserMediaOnly OrElse
                                                            (If(.Item("reblog")?.Count, 0) = 0 OrElse .Value({"reblog", "account"}, "id") = ID)) Then
                                        If If(.Item("media_attachments")?.Count, 0) > 0 Then
                                            s = .Item("media_attachments")
                                        Else
                                            s = .Item({"reblog"}, "media_attachments")
                                        End If
                                        If s.ListExists Then
                                            For Each ss In s : ObtainMedia(ss, PostID, PostDate) : Next
                                        End If
                                    End If
                                End With
                            Next
                        End If
                    End Using
                End If

                If POST.IsEmptyString And ExistsDetected Then Exit Sub
                If Not PostID.IsEmptyString And NewPostDetected Then DownloadData(PostID, Token)
            Catch ex As Exception
                ProcessException(ex, Token, $"data downloading error{IIf(IsSavedPosts, " (Saved Posts)", String.Empty)} [{URL}]")
            End Try
        End Sub
        Private Sub ObtainMedia(ByVal e As EContainer, ByVal PostID As String, ByVal PostDate As String, Optional ByVal BaseUrl As String = Nothing)
            Dim t As UTypes = UTypes.Undefined
            Select Case e.Value("type")
                Case "video" : t = UTypes.Video
                Case "image" : t = UTypes.Picture
                Case "gifv" : t = UTypes.GIF
            End Select
            If Not t = UTypes.Undefined Then
                Dim m As New UserMedia(e.Value("url"), t) With {
                    .Post = New UserPost(PostID, AConvert(Of Date)(PostDate, DateProvider, Nothing, EDP.ReturnValue)),
                    .URL_BASE = BaseUrl.IfNullOrEmpty(MySettings.GetUserPostUrl(Me, m))
                }
                If Not t = UTypes.GIF Or GifsDownload Then
                    If t = UTypes.GIF Then
                        If Not GifsSpecialFolder.IsEmptyString Then m.SpecialFolder = GifsSpecialFolder
                        If Not GifsPrefix.IsEmptyString Then m.File.Name = $"{GifsPrefix}{m.File.Name}"
                    End If
                    If Not m.URL.IsEmptyString Then _TempMediaList.ListAddValue(m, LNC)
                End If
            End If
        End Sub
        Private Sub ObtainUserID()
            Try
                If ID.IsEmptyString Then
                    Dim url$ = $"https://{MyCredentials.Domain}/api/v1/accounts/lookup?acct="
                    If Not UserDomain.IsEmptyString Then
                        If UserDomain = MyCredentials.Domain Then
                            url &= $"@{TrueName}"
                        Else
                            url &= $"@{TrueName}@{UserDomain}"
                        End If
                    Else
                        url &= $"@{TrueName}"
                    End If
                    Dim r$ = Responser.GetResponse(url)
                    If Not r.IsEmptyString Then
                        Using j As EContainer = JsonDocument.Parse(r)
                            If Not j Is Nothing Then ID = j.Value("id")
                        End Using
                    End If
                End If
            Catch ex As Exception
                ErrorsDescriber.Execute(EDP.SendToLog, ex, $"API.Mastodon.UserData.ObtainUserID({ToStringForLog()})")
            End Try
        End Sub
        Private Function GetSinglePostPattern(ByVal Domain As String) As String
            Return $"https://{Domain}/api/v1/statuses/" & "{0}"
        End Function
        Protected Overrides Sub ReparseMissing(ByVal Token As CancellationToken)
            SinglePostUrl = GetSinglePostPattern(MyCredentials.Domain)
            MyBase.ReparseMissing(Token)
        End Sub
#End Region
#Region "DownloadSingleObject"
        Protected Overrides Sub DownloadSingleObject_GetPosts(ByVal Data As IYouTubeMediaContainer, ByVal Token As CancellationToken)
            Dim PostID$ = RegexReplace(Data.URL, RParams.DM("(?<=/)\d+", 0, EDP.ReturnValue))
            If Not PostID.IsEmptyString Then
                ResetCredentials()
                Dim pattern$
                If Not ACheck(MySettings.MyDomain.Value) Then
                    Throw New ArgumentNullException("Mastodon domain", "Mastodon domain not set")
                Else
                    pattern = GetSinglePostPattern(MySettings.MyDomain.Value)
                End If
                Dim r$ = Responser.GetResponse(String.Format(pattern, PostID),, EDP.ReturnValue)
                If Not r.IsEmptyString Then
                    Using j As EContainer = JsonDocument.Parse(r)
                        If j.ListExists AndAlso j.Contains("media_attachments") Then
                            For Each jj As EContainer In j("media_attachments") : ObtainMedia(jj, PostID, String.Empty, Data.URL) : Next
                        End If
                    End Using
                End If
            End If
        End Sub
#End Region
#Region "Exception"
        Protected Overrides Function DownloadingException(ByVal ex As Exception, ByVal Message As String, Optional ByVal FromPE As Boolean = False,
                                                          Optional ByVal EObj As Object = Nothing) As Integer
            If TypeOf ex Is ArgumentNullException AndAlso Not ex.HelpLink.IsEmptyString And ex.HelpLink = 1 Then
                Return 0
            Else
                If Responser.Status = Net.WebExceptionStatus.NameResolutionFailure Then
                    MyMainLOG = $"User domain ({UserDomain}) not found: {ToStringForLog()}"
                    Return 1
                ElseIf Responser.StatusCode = Net.HttpStatusCode.NotFound Or Responser.StatusCode = Net.HttpStatusCode.Forbidden Then
                    UserExists = False
                    Return 1
                ElseIf Responser.StatusCode = Net.HttpStatusCode.Unauthorized Then
                    MyMainLOG = $"{ToStringForLog()}: account credentials have expired"
                    Return 2
                ElseIf Responser.StatusCode = Net.HttpStatusCode.Gone Then
                    UserSuspended = True
                    Return 1
                Else
                    Return 0
                End If
            End If
        End Function
#End Region
    End Class
End Namespace