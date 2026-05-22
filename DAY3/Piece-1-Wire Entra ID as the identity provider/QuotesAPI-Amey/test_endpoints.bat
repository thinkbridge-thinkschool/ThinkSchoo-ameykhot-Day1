@echo off
echo Testing POST /api/quotes > curl_output.txt
curl -s -i -X POST http://localhost:5000/api/quotes -H "Content-Type: application/json" -d "{\"author\":\"Albert Einstein\",\"text\":\"Imagination is more important than knowledge.\"}" >> curl_output.txt
echo. >> curl_output.txt
echo. >> curl_output.txt

echo Testing GET /api/quotes >> curl_output.txt
curl -s -i -X GET "http://localhost:5000/api/quotes?page=1&size=10" >> curl_output.txt
echo. >> curl_output.txt
echo. >> curl_output.txt

echo Testing GET /api/quotes/1 >> curl_output.txt
curl -s -i -X GET http://localhost:5000/api/quotes/1 >> curl_output.txt
echo. >> curl_output.txt
echo. >> curl_output.txt

echo Testing DELETE /api/quotes/1 >> curl_output.txt
curl -s -i -X DELETE http://localhost:5000/api/quotes/1 >> curl_output.txt
echo. >> curl_output.txt
