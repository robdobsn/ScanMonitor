class ScanManApp
    constructor: ->
        @docTouchMode = ""
        @showScroller()
        $(".nav-button").click ->
            $(this).stop().animate({backgroundColor:'#808080'}, 100).animate({backgroundColor:'#606060'}, 100)
        $("#nav-first-doc").click =>
            @showDoc(0)
        $("#nav-next-doc").click =>
            @showDoc(@curDocIdx + 1)
        $("#nav-prev-doc").click =>
            @showDoc(@curDocIdx - 1)
        $("#nav-last-doc").click =>
            @showDoc(100000)
        $("#docTypeTouch").click =>
            @docTouchMode = "DocType"
        $(".docTypeEdit").click =>
            @docTypeEdit()
        $(".pageImage").click (e) =>
            posX = e.pageX - $(e.currentTarget).offset().left
            posY = e.pageY - $(e.currentTarget).offset().top
            @setDocTypeFromRegion(posX, posY)

    go: ->
        @initDocs()

    showScroller: ->
        $('#docDateTime').scroller({
            preset: 'date',
            dateOrder: "ddMyy",
            showLabel: true,        # true or false
            theme: 'android',        # android or ios or sense-ui
            display: 'inline',  # modal or inline
            mode: 'clickpick',   # scroller or clickpick
            startYear: 1980,
            endYear: 2020,
        #    showOnFocus: true,
        #    onShow: (html, inst) ->
        #        d = new Date(2012, 4, 3, 8, 0)
        #        $('#testDateTime').scroller('setDate', d)
        })

    initDocs: ->
        @getScansInfo()

    getScansInfo: ->
        $.ajax 'http://localhost:8080/scandocs/list',
            type: 'GET'
            dataType: 'json'
            error: (jqXHR, textStatus, errorThrown) ->
                $('.statusBox').text "Can't connect to server: #{textStatus}"
            success: (data, textStatus, jqXHR) =>
                @scansInfo = data
                @curDocIdx = 0
                @showDocPage 1
                $('.statusBox').text "Ok"

    showDoc: (docIdx) ->
        if typeof(@scansInfo) == "undefined"
            return
        if @scansInfo.length == 0
            return
        if docIdx < 0
            docIdx = 0
        if docIdx >= @scansInfo.length
            docIdx = @scansInfo.length-1
        @curDocIdx = docIdx
        @showDocPage 1

    nextPage: ->
        if @curPageNum < @curDocInfo.PageCount
            @showDocPage @curPageNum + 1

    showDocPage: (pageNum) ->
        if typeof(@scansInfo) == "undefined"
            return
        @curPageNum = pageNum
        fileName = "file:" + @scansInfo[@curDocIdx].scanPageImageFileBase + "_" + @curPageNum + ".png"
        $(".pageImage").attr("src", fileName)
        @showDocInfo()

    showDocInfo: ->
        $(".docTypeBox").text @scansInfo[@curDocIdx].DetectedDocType
        $('#docDateTime').scroller("setDate", new Date(1990,10,2), true)

    setDocTypeFromRegion: (x,y) ->
        if typeof(@scansInfo) == "undefined"
            return
        uniqueName = @scansInfo[@curDocIdx].docName
        url = 'http://localhost:47294/api/scansinfo/doctype/' + uniqueName + "/" + x.toString() + "," + y.toString()
        $.ajax url,
            type: 'GET'
            dataType: 'json'
            error: (jqXHR, textStatus, errorThrown) ->
                $('.statusBox').text "Error doctypefromregion: #{textStatus}"
            success: (data, textStatus, jqXHR) =>
                $('.statusBox').text "+"
                @scansInfo[@curDocIdx].DetectedDocType = data["DocTypeName"]
                @showDocInfo()

    docTypeEdit: ->
        window.location.href = "doctype.html?docuniq=" + @scansInfo[@curDocIdx].docName

$(document).ready ->
    scanManApp = new ScanManApp()
    scanManApp.go()
