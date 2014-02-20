// Generated by CoffeeScript 1.6.3
var ScanManApp;

ScanManApp = (function() {
  function ScanManApp() {
    var _this = this;
    this.docTouchMode = "";
    this.showScroller();
    $(".nav-button").click(function() {
      return $(this).stop().animate({
        backgroundColor: '#808080'
      }, 100).animate({
        backgroundColor: '#606060'
      }, 100);
    });
    $("#nav-first-doc").click(function() {
      return _this.showDoc(0);
    });
    $("#nav-next-doc").click(function() {
      return _this.showDoc(_this.curDocIdx + 1);
    });
    $("#nav-prev-doc").click(function() {
      return _this.showDoc(_this.curDocIdx - 1);
    });
    $("#nav-last-doc").click(function() {
      return _this.showDoc(100000);
    });
    $("#docTypeTouch").click(function() {
      return _this.docTouchMode = "DocType";
    });
    $(".docTypeEdit").click(function() {
      return _this.docTypeEdit();
    });
    $(".pageImage").click(function(e) {
      var posX, posY;
      posX = e.pageX - $(e.currentTarget).offset().left;
      posY = e.pageY - $(e.currentTarget).offset().top;
      return _this.setDocTypeFromRegion(posX, posY);
    });
  }

  ScanManApp.prototype.go = function() {
    return this.initDocs();
  };

  ScanManApp.prototype.showScroller = function() {
    return $('#docDateTime').scroller({
      preset: 'date',
      dateOrder: "ddMyy",
      showLabel: true,
      theme: 'android',
      display: 'inline',
      mode: 'clickpick',
      startYear: 1980,
      endYear: 2020
    });
  };

  ScanManApp.prototype.initDocs = function() {
    return this.getScansInfo();
  };

  ScanManApp.prototype.getScansInfo = function() {
    var _this = this;
    return $.ajax('http://localhost:8080/scandocs/list', {
      type: 'GET',
      dataType: 'json',
      error: function(jqXHR, textStatus, errorThrown) {
        return $('.statusBox').text("Can't connect to server: " + textStatus);
      },
      success: function(data, textStatus, jqXHR) {
        _this.scansInfo = data;
        _this.curDocIdx = 0;
        _this.showDocPage(1);
        return $('.statusBox').text("Ok");
      }
    });
  };

  ScanManApp.prototype.showDoc = function(docIdx) {
    if (typeof this.scansInfo === "undefined") {
      return;
    }
    if (this.scansInfo.length === 0) {
      return;
    }
    if (docIdx < 0) {
      docIdx = 0;
    }
    if (docIdx >= this.scansInfo.length) {
      docIdx = this.scansInfo.length - 1;
    }
    this.curDocIdx = docIdx;
    return this.showDocPage(1);
  };

  ScanManApp.prototype.nextPage = function() {
    if (this.curPageNum < this.curDocInfo.PageCount) {
      return this.showDocPage(this.curPageNum + 1);
    }
  };

  ScanManApp.prototype.showDocPage = function(pageNum) {
    var fileName;
    if (typeof this.scansInfo === "undefined") {
      return;
    }
    this.curPageNum = pageNum;
    fileName = "file:" + this.scansInfo[this.curDocIdx].scanPageImageFileBase + "_" + this.curPageNum + ".png";
    $(".pageImage").attr("src", fileName);
    return this.showDocInfo();
  };

  ScanManApp.prototype.showDocInfo = function() {
    $(".docTypeBox").text(this.scansInfo[this.curDocIdx].DetectedDocType);
    return $('#docDateTime').scroller("setDate", new Date(1990, 10, 2), true);
  };

  ScanManApp.prototype.setDocTypeFromRegion = function(x, y) {
    var uniqueName, url,
      _this = this;
    if (typeof this.scansInfo === "undefined") {
      return;
    }
    uniqueName = this.scansInfo[this.curDocIdx].docName;
    url = 'http://localhost:47294/api/scansinfo/doctype/' + uniqueName + "/" + x.toString() + "," + y.toString();
    return $.ajax(url, {
      type: 'GET',
      dataType: 'json',
      error: function(jqXHR, textStatus, errorThrown) {
        return $('.statusBox').text("Error doctypefromregion: " + textStatus);
      },
      success: function(data, textStatus, jqXHR) {
        $('.statusBox').text("+");
        _this.scansInfo[_this.curDocIdx].DetectedDocType = data["DocTypeName"];
        return _this.showDocInfo();
      }
    });
  };

  ScanManApp.prototype.docTypeEdit = function() {
    return window.location.href = "doctype.html?docuniq=" + this.scansInfo[this.curDocIdx].docName;
  };

  return ScanManApp;

})();

$(document).ready(function() {
  var scanManApp;
  scanManApp = new ScanManApp();
  return scanManApp.go();
});
