import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../models/models';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private readonly baseUrl = 'http://localhost:5001/api';

  constructor(private http: HttpClient) {}

  private getHeaders(idempotencyKey?: string): HttpHeaders {
    let headers = new HttpHeaders({
      'Content-Type': 'application/json'
    });

    const token = localStorage.getItem('access_token');
    if (token) {
      headers = headers.set('Authorization', `Bearer ${token}`);
    }

    if (idempotencyKey) {
      headers = headers.set('Idempotency-Key', idempotencyKey);
    }

    return headers;
  }

  get<T>(path: string, params?: Record<string, any>): Observable<T> {
    let httpParams = new HttpParams();
    if (params) {
      Object.keys(params).forEach(key => {
        if (params[key] !== null && params[key] !== undefined && params[key] !== '') {
          httpParams = httpParams.set(key, params[key]);
        }
      });
    }

    return this.http.get<ApiResponse<T>>(`${this.baseUrl}/${path}`, {
      headers: this.getHeaders(),
      params: httpParams
    }).pipe(map(res => res.data));
  }

  post<T>(path: string, body: any, withIdempotency = false): Observable<T> {
    const idempotencyKey = withIdempotency ? crypto.randomUUID() : undefined;
    return this.http.post<ApiResponse<T>>(`${this.baseUrl}/${path}`, body, {
      headers: this.getHeaders(idempotencyKey)
    }).pipe(map(res => res.data));
  }

  put<T>(path: string, body: any): Observable<T> {
    return this.http.put<ApiResponse<T>>(`${this.baseUrl}/${path}`, body, {
      headers: this.getHeaders()
    }).pipe(map(res => res.data));
  }

  delete<T>(path: string): Observable<T> {
    return this.http.delete<ApiResponse<T>>(`${this.baseUrl}/${path}`, {
      headers: this.getHeaders()
    }).pipe(map(res => res.data));
  }
}
