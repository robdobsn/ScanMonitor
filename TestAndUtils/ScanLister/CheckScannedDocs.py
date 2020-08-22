from pymongo import MongoClient
import argparse
import os

class DBContext:
    sdi = None
    fdi = None
    def __init__(self, sdi, fdi):
        self.sdi = sdi
        self.fdi = fdi

class StatusCounts:
    statusCounts = {}
    def add(self, status):
        if status not in self.statusCounts:
            self.statusCounts[status] = 1
        else:
            self.statusCounts[status] += 1
    def getCounts(self):
        return self.statusCounts

def openDB(serverName):
    client = MongoClient(serverName,27017)
    db = client.ScanManager
    print(db.list_collection_names())
    sdi = db.ScanDocInfo
    fdi = db.FiledDocInfo
    print(sdi.count_documents({}))
    return DBContext(sdi, fdi)

def checkStatusOfDocsInFolder(folderName, dbContext):
    statusCounts = StatusCounts()
    # countDocs = 0
    for fileName in os.listdir(folderName):
        if fileName.endswith(".pdf"):
            # print(os.path.join(folderName, fileName))
            uniqName = fileName.split('.')[0]
            rslt = dbContext.sdi.find_one({"uniqName":uniqName})
            if not rslt:
                print(f"{fileName} is missing from database")
                statusCounts.add('missing')
                continue
            rslt = dbContext.fdi.find_one({"uniqName":uniqName})
            if not rslt:
                # print(f"{fileName} is unfiled")
                statusCounts.add('unfiled')
                continue
            finalStatus = rslt['filedAt_finalStatus']
            if finalStatus == 2:
                statusCounts.add('filed')
            elif finalStatus == 1:
                statusCounts.add('deleted')
            elif finalStatus == 3:
                statusCounts.add('deletedAfterEdit')
            else:
                print(f"{uniqName} rslt {rslt['filedAt_finalStatus']}")

            # countDocs += 1
            # if countDocs > 1000:
            #     break
    return statusCounts

parser = argparse.ArgumentParser()
parser.add_argument("foldername")
args = parser.parse_args()

dbContext = openDB('macallan')
statusCounts = checkStatusOfDocsInFolder(args.foldername, dbContext)
print("Document stats")
print(statusCounts.getCounts())


